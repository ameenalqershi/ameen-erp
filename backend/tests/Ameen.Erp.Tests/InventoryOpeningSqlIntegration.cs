using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class InventoryOpeningSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Guid branch, Actor owner, Actor writer, Actor reviewer)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        var sales = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-S", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default));
        var stock = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-I", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default));
        var cost = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-C", "تكلفة", AccountType.Expense, null, true, false, false, null, NormalBalance.Debit), default));
        var group = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(), new("OPEN-G", "مجموعة", sales.ResourceId, InventoryAccountId: stock.ResourceId, CostAccountId: cost.ResourceId), default));
        var unit = await Call(s => s.Unit.CreateUnit(cid, owner, Fixture.Key(), new("OPEN-U", "وحدة", "EA", 0, "وحدة"), default));
        var item = await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(), new("OPEN-ITEM", "صنف", ItemType.Goods, ItemTracking.None, group.ResourceId, unit.ResourceId, new[] { new ItemUnitRequest(unit.ResourceId, 1, null, null) }), default));
        Guid keeper; await using (var db = Context()) keeper = (await db.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == owner.Subject)).Id;
        var wh = await Call(s => s.Warehouse.CreateWarehouse(cid, owner, Fixture.Key(), new("OPEN-WH", "مخزن", branch, keeper, stock.ResourceId, WarehouseType.Standard, false, Array.Empty<WarehouseLocationRequest>()), default));
        var request = new CreateInventoryOpeningRequest(branch, wh.ResourceId, Fixture.Date, Fixture.Date, Fixture.Date, "SAR", 1, "SQL-COUNT", "افتتاح", new[] { new InventoryOpeningLineRequest(item.ResourceId, unit.ResourceId, 2, 5) });
        var key = Fixture.Key(); var replay = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.InventoryOpening.Create(cid, writer, key, request, default))));
        Ensure(replay.Distinct().Count() == 1, "Opening concurrent replay differs"); var r = replay[0];
        Ensure((await Call(s => s.InventoryOpening.Preview(cid, r.ResourceId, writer, default))).TotalValue == 10, "SQL valuation differs");
        var more = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Call(s => s.InventoryOpening.Create(cid, writer, Fixture.Key(), request, default))));
        Ensure(more.Select(x => x.Number).Append(r.Number).Distinct().Count() == 4, "Opening numbers duplicated");
        var edit = new EditInventoryOpeningRequest(r.Version, "معدل", new[] { request.Lines[0] with { Quantity = 3 } }, "تصحيح"); key = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_OpeningAudit] CHECK ([Action] <> 'inventory-opening.edited')");
        try { await Call(s => s.InventoryOpening.Edit(cid, r.ResourceId, writer, key, edit, default)); throw new Exception("Expected audit failure"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_OpeningAudit]");
            var saved = await db.Set<InventoryOpening>().SingleAsync(x => x.CompanyId == cid && x.Id == r.ResourceId);
            Ensure(saved.Version == r.Version && saved.Lines.Single().Quantity == 2, "Opening edit rollback failed");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed receipt persisted");
        }
        await using (var db = Context())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [opening_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [opening_runtime_test]; EXECUTE AS USER = 'opening_runtime_test';");
            try { var s = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()); r = await s.InventoryOpening.Edit(cid, r.ResourceId, writer, key, edit, default); }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [opening_runtime_test];"); }
        }
        Ensure((await Call(s => s.InventoryOpening.Get(cid, r.ResourceId, writer, default))).Lines.Single().Quantity == 3, "SQL line replacement failed");
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, writer, Fixture.Key(), new(r.Version), "submit", default));
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, reviewer, Fixture.Key(), new(r.Version, "تصحيح"), "return", default));
        r = await Call(s => s.InventoryOpening.Edit(cid, r.ResourceId, writer, Fixture.Key(), edit with { ExpectedVersion = r.Version }, default));
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, writer, Fixture.Key(), new(r.Version), "submit", default));
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, reviewer, Fixture.Key(), new(r.Version), "approve", default));
        await using (var db = Context())
        {
            var saved = await db.Set<InventoryOpening>().SingleAsync(x => x.CompanyId == cid && x.Id == r.ResourceId);
            Ensure(saved.State == InventoryOpeningState.Approved && saved.ApprovedBy == reviewer.Subject, "SQL approval failed");
            db.Remove(saved.Lines.Single());
            try { await db.SaveChangesAsync(); throw new Exception("Approved line deletion accepted"); }
            catch (BusinessException e) when (e.Code == "opening_lines_immutable") { }
        }
        var periodId = (await Call(s => s.InventoryOpening.Get(cid, r.ResourceId, writer, default))).FiscalPeriodId;
        var close = await Call(s => s.PeriodClose.PreviewPeriodClose(cid, periodId, reviewer, 0, 10, default));
        Ensure(close.PendingInventoryOpeningCount >= 4 && !close.CanCloseGeneralLedger, "Pending openings omitted from close checks");
        Ensure((await Call(s => s.InventoryOpening.Search(cid, writer, branch, InventoryOpeningState.Approved, "SQL-COUNT", 0, 10, default))).TotalCount == 1, "SQL opening search failed");
        Console.WriteLine("  SQL PASS INV06 concurrent replay/numbering, valuation, audit rollback, runtime line replacement, return/correct/approve, immutable lines and close blockers");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
