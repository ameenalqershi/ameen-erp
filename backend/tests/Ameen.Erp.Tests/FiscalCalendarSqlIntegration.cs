using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class FiscalCalendarSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var request = FiscalCalendarTests.Request(); var key = Fixture.Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, key, request, default))));
        Ensure(results.All(x => x == results[0]), "Calendar concurrent replay differs");
        var calendar = await Call(s => s.FiscalCalendar.GetFiscalCalendar(cid, results[0].ResourceId, owner, default));
        Ensure(calendar.Periods.Count == 2 && calendar.Periods[0].Code == "2027-H1", "Custom SQL calendar differs");
        // SYS03-03: a changed payload cannot reuse a successful calendar creation key,
        // even after the original receipt has been committed by a separate SQL context.
        await using (var beforeConflict = Context())
        {
            var yearCount = await beforeConflict.Set<FiscalYear>().CountAsync(x => x.CompanyId == cid);
            var periodCount = await beforeConflict.Set<FiscalPeriod>().CountAsync(x => x.CompanyId == cid);
            var auditCount = await beforeConflict.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            var receiptCount = await beforeConflict.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            try
            {
                await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, key,
                    request with { Code = "2027-DIFFERENT" }, default));
                throw new Exception("SYS03-03 expected SQL idempotency conflict");
            }
            catch (BusinessException e) when (e.Code == "idempotency_conflict") { }
            try
            {
                await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, key,
                    request with { Periods = new[] {
                        request.Periods![0] with { Code = "2027-FIRST" }, request.Periods[1]
                    } }, default));
                throw new Exception("SYS03-03 expected SQL changed period idempotency conflict");
            }
            catch (BusinessException e) when (e.Code == "idempotency_conflict") { }
            await using var afterConflict = Context();
            Ensure(await afterConflict.Set<FiscalYear>().CountAsync(x => x.CompanyId == cid) == yearCount &&
                await afterConflict.Set<FiscalPeriod>().CountAsync(x => x.CompanyId == cid) == periodCount &&
                await afterConflict.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == auditCount &&
                await afterConflict.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receiptCount,
                "SYS03-03 changed idempotency payload persisted SQL calendar, audit or receipt");
        }
        Console.WriteLine("  SQL PASS SYS03-03 changed fiscal year replay rejects atomically");
        // SYS03-AC01: a newly provisioned calendar opens two contiguous
        // posting periods with exact tenant scope and no implicit closure.
        Ensure(calendar.State == FiscalCalendarState.Open && calendar.GeneralLedgerOnly &&
            calendar.Periods.Count == 2 && calendar.Periods.All(x => !x.IsClosed) &&
            calendar.Periods[0].StartsOn == new DateOnly(2027, 1, 1) &&
            calendar.Periods[0].EndsOn == new DateOnly(2027, 6, 30) &&
            calendar.Periods[1].StartsOn == new DateOnly(2027, 7, 1) &&
            calendar.Periods[1].EndsOn == new DateOnly(2027, 12, 31),
            "SYS03-AC01 provisioned calendar not open/contiguous for posting");
        await using (var periodDb = Context())
        {
            var storedPeriods = await periodDb.Set<FiscalPeriod>()
                .Where(x => x.CompanyId == cid && x.FiscalYearId == calendar.Year.Id)
                .OrderBy(x => x.StartsOn).ToListAsync();
            Ensure(storedPeriods.Count == 2 &&
                storedPeriods.All(x => x.CompanyId == cid && !x.IsClosed) &&
                storedPeriods[0].Code == "2027-H1" &&
                storedPeriods[0].EndsOn == new DateOnly(2027, 6, 30) &&
                storedPeriods[1].Code == "2027-H2" &&
                storedPeriods[1].StartsOn == new DateOnly(2027, 7, 1),
                "SYS03-AC01 persisted SQL calendar dates or open period state differ");
        }
        Console.WriteLine("  SQL PASS SYS03-AC01 contiguous open posting periods and tenant scope");
        // SYS03-FG02: the API header's required start/end are persisted
        // exactly as DateOnly values, without truncation or shifting in SQL.
        Ensure(calendar.Year.StartsOn == new DateOnly(2027, 1, 1) &&
            calendar.Year.EndsOn == new DateOnly(2027, 12, 31),
            "SYS03-FG02 year range differs from the submitted dates");
        await using (var dateDb = Context())
        {
            var stored = await dateDb.Set<FiscalYear>().SingleAsync(x =>
                x.CompanyId == cid && x.Id == results[0].ResourceId);
            Ensure(stored.StartsOn == new DateOnly(2027, 1, 1) &&
                stored.EndsOn == new DateOnly(2027, 12, 31) &&
                stored.Code == "2027",
                "SYS03-FG02 persisted fiscal dates/code differ in tenant SQL");
        }
        Console.WriteLine("  SQL PASS SYS03-FG02 fiscal start/end DateOnly roundtrip in SQL");
        // SYS03-FG03 (F07-F10): a period's code is text, not a number;
        // zero-prefixed identifiers and system-owned open state survive SQL.
        var coded = await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, Fixture.Key(),
            new("2034", new(2034, 1, 1), new(2034, 12, 31), new[] {
                new FiscalPeriodRequest("0001", new(2034, 1, 1), new(2034, 6, 30)),
                new FiscalPeriodRequest("0002", new(2034, 7, 1), new(2034, 12, 31))
            }), default));
        await using (var periodFieldsDb = Context())
        {
            var rows = await periodFieldsDb.Set<FiscalPeriod>()
                .Where(x => x.CompanyId == cid && x.FiscalYearId == coded.ResourceId)
                .OrderBy(x => x.StartsOn).ToListAsync();
            Ensure(rows.Count == 2 && rows.All(x => !x.IsClosed && x.CompanyId == cid) &&
                rows[0].Code == "0001" && rows[0].StartsOn == new DateOnly(2034, 1, 1) &&
                rows[0].EndsOn == new DateOnly(2034, 6, 30) &&
                rows[1].Code == "0002" && rows[1].StartsOn == new DateOnly(2034, 7, 1) &&
                rows[1].EndsOn == new DateOnly(2034, 12, 31),
                "SYS03-FG03 stored period code/dates or system-owned state differ");
        }
        Console.WriteLine("  SQL PASS SYS03-FG03 leading-zero period code, dates, open state and company isolation");
        Ensure((await Call(s => s.FiscalCalendar.SearchFiscalYears(cid, owner, "2027", 0, 10, default))).TotalCount == 1, "Calendar SQL search differs");
        Ensure((await Call(s => s.FiscalCalendar.ListFiscalYearAudit(cid, results[0].ResourceId, owner, 0, 10, default))).TotalCount == 1, "Duplicate calendar audit");
        Ensure((await Call(s => s.FiscalCalendar.GetFiscalYearUsage(cid, results[0].ResourceId, owner, default))).JournalCount == 0, "New calendar has usage");
        // SYS03-OP03: the active SQL tenant owns the fiscal-year audit event,
        // and usage for a newly created calendar has no journal or generation.
        await using (var auditDb = Context())
        {
            var audit = await auditDb.Set<AuditEvent>()
                .Where(x => x.CompanyId == cid && x.ResourceId == results[0].ResourceId &&
                    x.Action == "fiscal-year.created").SingleAsync();
            Ensure(audit.ActorId == owner.Subject && audit.OccurredAt != default,
                "SYS03-OP03 SQL actor/time or tenant-scoped fiscal audit differs");
        }
        var fiscalUsage = await Call(s => s.FiscalCalendar.GetFiscalYearUsage(cid, results[0].ResourceId, owner, default));
        Ensure(fiscalUsage.FiscalYearId == results[0].ResourceId &&
            fiscalUsage.JournalCount == 0 && fiscalUsage.UnpostedJournalCount == 0 &&
            fiscalUsage.RecurringGenerationCount == 0,
            "SYS03-OP03 SQL usage counts differ");
        Console.WriteLine("  SQL PASS SYS03-OP03 persisted audit and zero-usage calendar snapshot");
        // SYS03-AC02: direct SQL contract rejects a one-day gap without
        // saving a fiscal year, its periods, an audit or an idempotency receipt.
        var gapKey = Fixture.Key();
        var auditsBeforeGap = 0;
        await using (var beforeGap = Context())
            auditsBeforeGap = await beforeGap.Set<AuditEvent>().CountAsync(x =>
                x.CompanyId == cid && x.Action == "fiscal-year.created");
        try
        {
            await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, gapKey,
                new("2032-GAP", new(2032, 1, 1), new(2032, 12, 31), new[] {
                    new FiscalPeriodRequest("2032-P1", new(2032, 1, 1), new(2032, 6, 30)),
                    new FiscalPeriodRequest("2032-P2", new(2032, 7, 2), new(2032, 12, 31))
                }), default));
            throw new Exception("SYS03-AC02 SQL accepted a gap between fiscal periods");
        }
        catch (BusinessException e) when (e.Code == "invalid_period_coverage") { }
        await using (var afterGap = Context())
            Ensure(!await afterGap.Set<FiscalYear>().AnyAsync(x => x.CompanyId == cid && x.Code == "2032-GAP") &&
                !await afterGap.Set<FiscalPeriod>().AnyAsync(x => x.CompanyId == cid && x.Code.StartsWith("2032-P")) &&
                !await afterGap.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == gapKey) &&
                await afterGap.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.Action == "fiscal-year.created") == auditsBeforeGap,
                "SYS03-AC02 SQL rejected gap persisted a partial calendar, audit or receipt");
        Console.WriteLine("  SQL PASS SYS03-AC02 uncovered period gap rejected atomically");
        async Task<string> Create(string code)
        {
            try { await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, Fixture.Key(), new(code, new(2028, 1, 1), new(2028, 12, 31)), default)); return "ok"; }
            catch (BusinessException e) { return e.Code; }
        }
        var competing = await Task.WhenAll(Create("2028-A"), Create("2028-B"));
        Ensure(competing.Count(x => x == "ok") == 1 && competing.Count(x => x == "fiscal_year_overlap") == 1, "Concurrent overlapping years accepted");
        var retry = new CreateFiscalYearRequest("2029", new(2029, 1, 1), new(2029, 12, 31),
            new[] { new FiscalPeriodRequest("2029-ONLY", new(2029, 1, 1), new(2029, 12, 31)) });
        var retryKey = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_CalendarAudit] CHECK ([Action] <> 'fiscal-year.created')");
        try { await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner, retryKey, retry, default)); throw new Exception("Expected failure"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_CalendarAudit]");
            Ensure(!await db.Set<FiscalYear>().AnyAsync(x => x.CompanyId == cid && x.Code == "2029") &&
                !await db.Set<FiscalPeriod>().AnyAsync(x => x.CompanyId == cid && x.Code == "2029-ONLY") &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == retryKey), "Failed calendar saved partial data");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [calendar_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [calendar_runtime_test]; EXECUTE AS USER = 'calendar_runtime_test';");
            try
            {
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                await service.FiscalCalendar.CreateFiscalYear(cid, owner, retryKey, retry, default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [calendar_runtime_test];"); }
        }
        Console.WriteLine("  SQL PASS SYS03 custom periods, concurrent replay/overlap protection, SQL reads, atomic rollback and runtime-role retry");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
