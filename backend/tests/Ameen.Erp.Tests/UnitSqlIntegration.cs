using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class UnitSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var key = Fixture.Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.Unit.CreateUnit(cid, owner, key, UnitTests.Request(), default))));
        Ensure(results.All(x => x == results[0]), "Concurrent unit replay differs");
        var id = results[0].ResourceId;
        var unit = await Call(s => s.Unit.GetUnit(cid, id, owner, default));
        var edit = UnitTests.Edit(unit) with { QuantityScale = 3 }; key = Fixture.Key();
        results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.Unit.EditUnit(cid, id, owner, key, edit, default))));
        Ensure(results.All(x => x == results[0]), "Concurrent unit edit replay differs");
        unit = await Call(s => s.Unit.GetUnit(cid, id, owner, default)); Ensure(unit.Revision == 2, "Unit revision incremented twice");
        Ensure((await Call(s => s.Unit.SearchUnits(cid, owner, "Each", true, 0, 10, default))).TotalCount == 1, "Unit SQL search differs");
        edit = UnitTests.Edit(unit) with { Notes = "retry" }; key = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_UnitAudit] CHECK ([Action] <> 'unit.edited')");
        try { await Call(s => s.Unit.EditUnit(cid, id, owner, key, edit, default)); throw new Exception("Expected audit failure"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_UnitAudit]");
            var saved = await db.Set<UnitOfMeasure>().SingleAsync(x => x.CompanyId == cid && x.Id == id);
            Ensure(saved.Version == unit.Version && saved.Notes == unit.Notes && saved.Revision == 2, "Failed unit edit persisted");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed unit edit saved receipt");
        }
        await Call(s => s.Unit.EditUnit(cid, id, owner, key, edit, default));
        await using (var db = Context())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [unit_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [unit_runtime_test]; EXECUTE AS USER = 'unit_runtime_test';");
            try
            {
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                var current = await service.Unit.GetUnit(cid, id, owner, default);
                await service.Unit.SetUnitStatus(cid, id, owner, Fixture.Key(), new(current.Version, false, "إيقاف"), default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [unit_runtime_test];"); }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UnitsOfMeasure] SET [QuantityScale]=7 WHERE [CompanyId]={cid} AND [Id]={id}"); throw new Exception("SQL accepted invalid scale"); }
            catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
        }
        Ensure((await Call(s => s.Unit.ListUnitAudit(cid, id, owner, 0, 10, default))).TotalCount == 4, "Unit SQL audit differs");
        Console.WriteLine("  SQL PASS INV03 concurrent creation/edit replay, SQL search, audit rollback/retry, runtime role and precision constraint");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
