using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class ItemSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<ErpService, Task<T>> work)
        { await using var db = Context(); return await work(new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        var sales = await Call(s => s.CreateAccount(cid, owner, Fixture.Key(), new("ITEMSQL-S", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default));
        var group = await Call(s => s.CreateItemGroup(cid, owner, Fixture.Key(), new("ITEMSQL-G", "خدمات", sales.ResourceId), default));
        var unit = await Call(s => s.CreateUnit(cid, owner, Fixture.Key(), new("ITEMSQL-U", "وحدة", "EA", 0, "وحدة"), default));
        var request = new CreateItemRequest("ITEMSQL", "خدمة", ItemType.Service, ItemTracking.None, group.ResourceId, unit.ResourceId,
            new[] { new ItemUnitRequest(unit.ResourceId, 1m, "000-ITEMSQL", 5m) });
        var key = Fixture.Key(); var replays = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.CreateItem(cid, owner, key, request, default))));
        Ensure(replays.All(x => x == replays[0]), "Item concurrent replay differs");
        var item = await Call(s => s.GetItem(cid, replays[0].ResourceId, owner, default));
        var edit = ItemTests.Edit(item) with { Name = "معدل", Units = new[] { request.Units[0] with { SalePrice = 6m } } }; key = Fixture.Key();
        replays = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.EditItem(cid, item.Id, owner, key, edit, default))));
        Ensure(replays.All(x => x == replays[0]), "Item edit replay differs");
        item = await Call(s => s.GetItem(cid, item.Id, owner, default));
        Ensure(item.Revision == 2 && item.Units.Count == 1 && item.Units[0].SalePrice == 6m, "SQL item line replacement differs");
        Ensure((await Call(s => s.SearchItems(cid, owner, "000-ITEMSQL", group.ResourceId, true, 0, 10, default))).TotalCount == 1, "SQL barcode search differs");
        Ensure((await Call(s => s.ListUnitItems(cid, unit.ResourceId, owner, 0, 10, default))).TotalCount == 1, "SQL unit usage differs");
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_ItemAudit] CHECK ([Action] <> 'item.edited')");
        edit = ItemTests.Edit(item) with { Units = new[] { request.Units[0] with { Barcode = "RETRY-ITEMSQL", SalePrice = 7m } } }; key = Fixture.Key();
        try { await Call(s => s.EditItem(cid, item.Id, owner, key, edit, default)); throw new Exception("Expected failure"); } catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_ItemAudit]");
            var saved = await db.Set<Item>().SingleAsync(x => x.CompanyId == cid && x.Id == item.Id);
            Ensure(saved.Version == item.Version && saved.Units.Single().Barcode == "000-ITEMSQL", "Failed replacement changed lines");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed item receipt saved");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [item_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [item_runtime_test]; EXECUTE AS USER = 'item_runtime_test';");
            try { var service = new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()); await service.EditItem(cid, item.Id, owner, key, edit, default); }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [item_runtime_test];"); }
        }
        async Task<string> Compete(string code)
        {
            try { await Call(s => s.CreateItem(cid, owner, Fixture.Key(), request with { Code = code, Units = new[] { request.Units[0] with { Barcode = "RACE-ITEMSQL" } } }, default)); return "ok"; }
            catch (BusinessException e) { return e.Code; }
        }
        var race = await Task.WhenAll(Compete("RACE-A"), Compete("RACE-B"));
        Ensure(race.Count(x => x == "ok") == 1 && race.Count(x => x == "duplicate_barcode") == 1, "Barcode race duplicated items");
        var other = Guid.NewGuid(); await Call(s => s.CreateCompany(owner, Fixture.Key(), new(other, "ITEMSQL-OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default));
        var foreign = await Call(s => s.CreateUnit(other, owner, Fixture.Key(), new("U", "وحدة", "EA", 0, "وحدة"), default));
        await using (var db = Context())
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ItemUnits] SET [UnitId]={foreign.ResourceId} WHERE [CompanyId]={cid} AND [ItemId]={item.Id}"); throw new Exception("Foreign item unit accepted"); }
            catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
        }
        Console.WriteLine("  SQL PASS INV04 concurrent replay, line replacement, barcode race, audit rollback, runtime role and cross-company unit FK");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
