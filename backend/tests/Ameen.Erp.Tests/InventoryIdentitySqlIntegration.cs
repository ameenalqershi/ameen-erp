using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class InventoryIdentitySqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        var sales = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("IDENT-S", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default));
        var stock = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("IDENT-I", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default));
        var cost = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("IDENT-C", "تكلفة", AccountType.Expense, null, true, false, false, null, NormalBalance.Debit), default));
        var group = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(), new("IDENT-G", "مجموعة", sales.ResourceId, InventoryAccountId: stock.ResourceId, CostAccountId: cost.ResourceId), default));
        var unit = await Call(s => s.Unit.CreateUnit(cid, owner, Fixture.Key(), new("IDENT-U", "وحدة", "EA", 0, "وحدة"), default));
        var item = await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(), new("IDENT", "صنف", ItemType.Goods, ItemTracking.Lot, group.ResourceId, unit.ResourceId, new[] { new ItemUnitRequest(unit.ResourceId, 1, null, null) }), default));
        var request = new CreateInventoryIdentityRequest("BATCH", "دفعة", new(2026, 1, 1), new(2027, 1, 1), InventoryQualityStatus.Quarantine);
        var key = Fixture.Key();
        var replay = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.InventoryIdentity.CreateLot(cid, item.ResourceId, owner, key, request, default))));
        Ensure(replay.Distinct().Count() == 1, "Concurrent identity replay differs");
        async Task<string> Compete()
        {
            try { await Call(s => s.InventoryIdentity.CreateLot(cid, item.ResourceId, owner, Fixture.Key(), request with { Code = "RACE" }, default)); return "ok"; }
            catch (BusinessException e) { return e.Code; }
        }
        var race = await Task.WhenAll(Compete(), Compete());
        Ensure(race.Count(x => x == "ok") == 1 && race.Count(x => x == "duplicate_inventory_identity") == 1, "Duplicate identity race");
        Ensure((await Call(s => s.InventoryIdentity.SearchLots(cid, item.ResourceId, owner, "BATCH", InventoryQualityStatus.Quarantine, true, 0, 1, default))).TotalCount == 1, "SQL identity search differs");
        var r = replay[0];
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_IdentityAudit] CHECK ([Action] <> 'inventory-lot.status-changed')");
        key = Fixture.Key();
        try { await Call(s => s.InventoryIdentity.SetLotStatus(cid, item.ResourceId, r.ResourceId, owner, key, new(r.Version, false, "إيقاف"), default)); throw new Exception("Expected audit failure"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_IdentityAudit]");
            var saved = await db.Set<InventoryIdentity>().SingleAsync(x => x.Id == r.ResourceId);
            Ensure(saved.Version == r.Version && saved.IsActive, "Failed audit mutated identity");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed identity receipt persisted");
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [InventoryIdentities] SET [ExpiryDate]=[ProductionDate] WHERE [Id]={r.ResourceId}"); throw new Exception("Invalid date persisted"); }
            catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [InventoryIdentities] SET [ItemId]={Guid.NewGuid()} WHERE [Id]={r.ResourceId}"); throw new Exception("Foreign item persisted"); }
            catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
        }
        await Call(s => s.InventoryIdentity.SetLotStatus(cid, item.ResourceId, r.ResourceId, owner, key, new(r.Version, false, "إيقاف"), default));
        Console.WriteLine("  SQL PASS INV05 replay, uniqueness race, filters, audit rollback, retry, expiry and item FK");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
