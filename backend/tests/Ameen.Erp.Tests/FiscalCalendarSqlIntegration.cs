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
