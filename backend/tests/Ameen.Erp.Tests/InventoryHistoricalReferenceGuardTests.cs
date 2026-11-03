using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

/// <summary>
/// The original movement and its reversal remain durable historical references:
/// zero net quantity does not make their warehouse or lot identities editable.
/// </summary>
public static class InventoryHistoricalReferenceGuardTests
{
    private static async Task<CreateInventoryOpeningRequest> ReversedSource(Fixture f, ItemTracking tracking)
    {
        var (request, approved) = await InventoryOpeningPostingTests.Setup(f, tracking);
        var posted = await InventoryOpeningPostingTests.Post(f, approved,
            await InventoryOpeningPostingTests.Plan(f, approved));
        var writer = InventoryOpeningTests.Writer;
        var membership = f.Store.All<Membership>().Single(x => x.Subject == writer.Subject);
        await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
            new(writer.Subject, membership.GrantedPermissions.Append(Permissions.InventoryReverse).Distinct().ToArray(),
                true, Array.Empty<Guid>(), membership.Version), default);
        var draft = await f.Service.InventoryOpening.CreateReversal(f.CompanyId, posted.ResourceId, writer,
            Fixture.Key(), new(posted.Version, Fixture.Date, "حماية المراجع التاريخية"), default);
        draft = await InventoryOpeningTests.Action(f, draft, "submit");
        draft = await InventoryOpeningTests.Action(f, draft, "approve", new Actor("stock-reviewer"));
        await f.Service.InventoryOpening.PostReversal(f.CompanyId, draft.ResourceId, writer,
            Fixture.Key(), new(draft.Version), default);
        Ensure(f.Store.Count<StockMovement>() == 2 && f.Store.Count<JournalEntry>() == 2 &&
            f.Store.All<StockMovement>().Sum(x => x.BaseQuantity) == 0m &&
            f.Store.All<StockMovement>().Sum(x => x.Value) == 0m);
        return request;
    }

    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV01-GAP02: reversed stock still locks warehouse structure but permits descriptive edit", async f =>
        {
            var source = await ReversedSource(f, ItemTracking.None);
            var warehouse = await f.Service.Warehouse.GetWarehouse(f.CompanyId, source.WarehouseId, f.Owner, default);
            var beforeAudits = f.Store.Count<AuditEvent>();
            var beforeReceipts = f.Store.Count<IdempotencyRecord>();
            var beforeOutbox = f.Store.Count<OutboxEvent>();
            var beforeJournals = f.Store.Count<JournalEntry>();
            var edit = WarehouseTests.Edit(warehouse);

            await Error("warehouse_stock_structure_in_use", () => f.Service.Warehouse.EditWarehouse(
                f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(), edit with { Code = "RECLASSIFIED" }, default));
            await Error("warehouse_stock_structure_in_use", () => f.Service.Warehouse.EditWarehouse(
                f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(), edit with { Type = WarehouseType.Quarantine }, default));
            await Error("warehouse_stock_structure_in_use", () => f.Service.Warehouse.EditWarehouse(
                f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(), edit with { TrackLocations = true,
                    Locations = new[] { new WarehouseLocationRequest("L01", "موقع جديد") } }, default));

            var saved = await f.Service.Warehouse.GetWarehouse(f.CompanyId, warehouse.Id, f.Owner, default);
            Ensure(saved.Version == warehouse.Version && saved.Revision == warehouse.Revision &&
                !saved.TrackLocations && saved.Type == WarehouseType.Standard &&
                f.Store.Count<AuditEvent>() == beforeAudits && f.Store.Count<IdempotencyRecord>() == beforeReceipts &&
                f.Store.Count<OutboxEvent>() == beforeOutbox && f.Store.Count<JournalEntry>() == beforeJournals &&
                f.Store.Count<StockMovement>() == 2);

            var changed = await f.Service.Warehouse.EditWarehouse(f.CompanyId, warehouse.Id, f.Owner,
                Fixture.Key(), edit with { Name = "تحديث الوصف بعد العكس" }, default);
            saved = await f.Service.Warehouse.GetWarehouse(f.CompanyId, warehouse.Id, f.Owner, default);
            Ensure(saved.Name == "تحديث الوصف بعد العكس" && saved.Version == changed.Version &&
                saved.Code == warehouse.Code && f.Store.Count<StockMovement>() == 2 &&
                f.Store.Count<JournalEntry>() == beforeJournals && f.Store.Count<OutboxEvent>() == beforeOutbox);
        });

        test("INV05-GAP03: fully reversed lot remains historically numbered and dated", async f =>
        {
            var source = await ReversedSource(f, ItemTracking.Lot);
            var line = source.Lines.Single();
            var identity = await f.Service.InventoryIdentity.GetLot(f.CompanyId,
                line.ItemId, line.IdentityId!.Value, f.Owner, default);
            var original = new EditInventoryIdentityRequest(identity.Version, identity.Code, identity.Name,
                identity.ProductionDate, identity.ExpiryDate, identity.QualityStatus,
                identity.EnglishName, identity.Notes, "حماية الدفعة", identity.LotNumber);
            var beforeAudits = f.Store.Count<AuditEvent>();
            var beforeReceipts = f.Store.Count<IdempotencyRecord>();
            var beforeOutbox = f.Store.Count<OutboxEvent>();
            var beforeJournals = f.Store.Count<JournalEntry>();

            await Error("inventory_identity_in_stock", () => f.Service.InventoryIdentity.EditLot(
                f.CompanyId, line.ItemId, identity.Id, f.Owner, Fixture.Key(),
                original with { Code = "HISTORIC-RENUMBER" }, default));
            await Error("inventory_identity_in_stock", () => f.Service.InventoryIdentity.EditLot(
                f.CompanyId, line.ItemId, identity.Id, f.Owner, Fixture.Key(),
                original with { LotNumber = "HISTORIC-LOT" }, default));
            await Error("inventory_identity_in_stock", () => f.Service.InventoryIdentity.EditLot(
                f.CompanyId, line.ItemId, identity.Id, f.Owner, Fixture.Key(),
                original with { ExpiryDate = Fixture.Date.AddDays(40) }, default));

            var saved = await f.Service.InventoryIdentity.GetLot(f.CompanyId,
                line.ItemId, identity.Id, f.Owner, default);
            Ensure(saved.Version == identity.Version && saved.Code == identity.Code &&
                saved.LotNumber == identity.LotNumber && saved.ExpiryDate == identity.ExpiryDate &&
                f.Store.Count<AuditEvent>() == beforeAudits && f.Store.Count<IdempotencyRecord>() == beforeReceipts &&
                f.Store.Count<OutboxEvent>() == beforeOutbox && f.Store.Count<JournalEntry>() == beforeJournals &&
                f.Store.Count<StockMovement>() == 2);

            var edited = await f.Service.InventoryIdentity.EditLot(f.CompanyId, line.ItemId, identity.Id,
                f.Owner, Fixture.Key(), original with { Name = "اسم توثيقي بعد العكس" }, default);
            saved = await f.Service.InventoryIdentity.GetLot(f.CompanyId,
                line.ItemId, identity.Id, f.Owner, default);
            Ensure(saved.Name == "اسم توثيقي بعد العكس" && saved.Version == edited.Version &&
                saved.Code == identity.Code && saved.LotNumber == identity.LotNumber &&
                f.Store.Count<StockMovement>() == 2 &&
                f.Store.Count<JournalEntry>() == beforeJournals && f.Store.Count<OutboxEvent>() == beforeOutbox);
        });
    }

    private static void Ensure(bool success)
    {
        if (!success) throw new Exception("Inventory historical reference guard failed");
    }

    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected historical stock guard rejection: " + code);
    }
}
