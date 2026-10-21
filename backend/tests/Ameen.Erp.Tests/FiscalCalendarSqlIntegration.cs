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
        async Task<T> Call<T>(Func<ErpService, Task<T>> work)
        {
            await using var db = Context(); return await work(new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var request = FiscalCalendarTests.Request(); var key = Fixture.Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.CreateFiscalYear(cid, owner, key, request, default))));
        Ensure(results.All(x => x == results[0]), "Calendar concurrent replay differs");
        var calendar = await Call(s => s.GetFiscalCalendar(cid, results[0].ResourceId, owner, default));
        Ensure(calendar.Periods.Count == 2 && calendar.Periods[0].Code == "2027-H1", "Custom SQL calendar differs");
        Ensure((await Call(s => s.SearchFiscalYears(cid, owner, "2027", 0, 10, default))).TotalCount == 1, "Calendar SQL search differs");
        Ensure((await Call(s => s.ListFiscalYearAudit(cid, results[0].ResourceId, owner, 0, 10, default))).TotalCount == 1, "Duplicate calendar audit");
        Ensure((await Call(s => s.GetFiscalYearUsage(cid, results[0].ResourceId, owner, default))).JournalCount == 0, "New calendar has usage");
        async Task<string> Create(string code)
        {
            try { await Call(s => s.CreateFiscalYear(cid, owner, Fixture.Key(), new(code, new(2028, 1, 1), new(2028, 12, 31)), default)); return "ok"; }
            catch (BusinessException e) { return e.Code; }
        }
        var competing = await Task.WhenAll(Create("2028-A"), Create("2028-B"));
        Ensure(competing.Count(x => x == "ok") == 1 && competing.Count(x => x == "fiscal_year_overlap") == 1, "Concurrent overlapping years accepted");
        var retry = new CreateFiscalYearRequest("2029", new(2029, 1, 1), new(2029, 12, 31),
            new[] { new FiscalPeriodRequest("2029-ONLY", new(2029, 1, 1), new(2029, 12, 31)) });
        var retryKey = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_CalendarAudit] CHECK ([Action] <> 'fiscal-year.created')");
        try { await Call(s => s.CreateFiscalYear(cid, owner, retryKey, retry, default)); throw new Exception("Expected failure"); }
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
                var service = new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                await service.CreateFiscalYear(cid, owner, retryKey, retry, default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [calendar_runtime_test];"); }
        }
        Console.WriteLine("  SQL PASS SYS03 custom periods, concurrent replay/overlap protection, SQL reads, atomic rollback and runtime-role retry");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
