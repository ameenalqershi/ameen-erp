using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class InventoryStockStructuralGuardTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV04-GAP03: a reversed inventory movement still locks item identity and unit structure", async f =>
        {
            var (request, approved) = await InventoryOpeningPostingTests.Setup(f, ItemTracking.Lot);
            var posted = await InventoryOpeningPostingTests.Post(f, approved,
                await InventoryOpeningPostingTests.Plan(f, approved));
            var writer = InventoryOpeningTests.Writer;
            var membership = f.Store.All<Membership>().Single(x => x.Subject == writer.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(writer.Subject, membership.GrantedPermissions.Append(Permissions.InventoryReverse).ToArray(),
                    true, Array.Empty<Guid>(), membership.Version), default);
            var reverse = await f.Service.InventoryOpening.CreateReversal(f.CompanyId, posted.ResourceId,
                writer, Fixture.Key(), new(posted.Version, Fixture.Date, "اختبار بقاء قفل الصنف"), default);
            reverse = await InventoryOpeningTests.Action(f, reverse, "submit");
            reverse = await InventoryOpeningTests.Action(f, reverse, "approve", new Actor("stock-reviewer"));
            await f.Service.InventoryOpening.PostReversal(f.CompanyId, reverse.ResourceId, writer,
                Fixture.Key(), new(reverse.Version), default);
            Ensure(f.Store.Count<StockMovement>() == 2 &&
                f.Store.All<StockMovement>().Sum(x => x.BaseQuantity) == 0 &&
                f.Store.All<StockMovement>().Sum(x => x.Value) == 0);

            var itemId = request.Lines.Single().ItemId;
            var item = await f.Service.Item.GetItem(f.CompanyId, itemId, f.Owner, default);
            var beforeAudit = f.Store.Count<AuditEvent>();
            var beforeReceipts = f.Store.Count<IdempotencyRecord>();
            var beforeJournals = f.Store.Count<JournalEntry>();
            var beforeOutbox = f.Store.Count<OutboxEvent>();
            var edited = ItemTests.Edit(item);
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId,
                itemId, f.Owner, Fixture.Key(), edited with { Code = "AFTER-REVERSE" }, default));
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId,
                itemId, f.Owner, Fixture.Key(), edited with { Type = ItemType.Service,
                    Tracking = ItemTracking.None, ShelfLifeDays = null, ReorderLevel = null }, default));
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId,
                itemId, f.Owner, Fixture.Key(), edited with { Tracking = ItemTracking.Serial }, default));
            var differentUnits = item.Units.Select(x => new ItemUnitRequest(x.UnitId,
                x.FactorToBase + 1, x.Barcode, x.SalePrice)).ToArray();
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId,
                itemId, f.Owner, Fixture.Key(), edited with { Units = differentUnits }, default));
            Ensure((await f.Service.Item.GetItem(f.CompanyId, itemId, f.Owner, default)).Version == item.Version &&
                f.Store.Count<AuditEvent>() == beforeAudit && f.Store.Count<IdempotencyRecord>() == beforeReceipts &&
                f.Store.Count<JournalEntry>() == beforeJournals && f.Store.Count<OutboxEvent>() == beforeOutbox &&
                f.Store.Count<StockMovement>() == 2);
        });
    }

    private static void Ensure(bool success)
    {
        if (!success) throw new Exception("Inventory item post-reversal immutability guard failed");
    }

    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected stock structure guard rejection: " + code);
    }
}
