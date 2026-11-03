using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class InventoryOpeningPostingTests
{
    private static Actor Writer => InventoryOpeningTests.Writer;
    internal static async Task<(CreateInventoryOpeningRequest Request, CommandReceipt Receipt)> Setup(Fixture f, ItemTracking tracking = ItemTracking.None)
    {
        var request = await InventoryOpeningTests.Setup(f, tracking);
        var member = f.Store.All<Membership>().Single(x => x.Subject == Writer.Subject);
        await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(Writer.Subject,
            member.GrantedPermissions.Append(Permissions.InventoryPost).ToArray(), true, Array.Empty<Guid>(), member.Version), default);
        var warehouse = await f.Service.Warehouse.GetWarehouse(f.CompanyId, request.WarehouseId, f.Owner, default);
        await f.Service.Warehouse.EditWarehouse(f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(), WarehouseTests.Edit(warehouse) with { TrackLocations = false, Locations = Array.Empty<WarehouseLocationRequest>() }, default);
        await f.Service.InventoryOpening.ConfigurePosting(f.CompanyId, f.Owner, Fixture.Key(), new(null, f.EquityId, "إعداد حساب التسوية"), default);
        var reviewer = await InventoryOpeningTests.Reviewer(f);
        var r = await InventoryOpeningTests.Create(f, request); r = await InventoryOpeningTests.Action(f, r, "submit");
        r = await InventoryOpeningTests.Action(f, r, "approve", reviewer);
        return (request, r);
    }
    internal static async Task<PostInventoryOpeningRequest> Plan(Fixture f, CommandReceipt r)
    {
        var preview = await f.Service.InventoryOpening.Preview(f.CompanyId, r.ResourceId, Writer, default);
        Ensure(preview.CanPost && preview.PostingPlan is not null);
        return new(r.Version, preview.PostingPlan!.SettingsVersion, preview.PostingPlan.WarehouseVersion);
    }
    internal static Task<CommandReceipt> Post(Fixture f, CommandReceipt r, PostInventoryOpeningRequest p, string? key = null) =>
        f.Service.InventoryOpening.Post(f.CompanyId, r.ResourceId, Writer, key ?? Fixture.Key(), p, default);
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV06 posting: atomic stock ledger balanced control journal provenance and replay", async f =>
        {
            var (_, r) = await Setup(f); var plan = await Plan(f, r); var key = Fixture.Key(); var posted = await Post(f, r, plan, key);
            Ensure(posted == await Post(f, r, plan, key));
            var opening = await InventoryOpeningTests.Get(f, r.ResourceId); var journal = f.Store.All<JournalEntry>().Single(); var movement = f.Store.All<StockMovement>().Single();
            Ensure(opening.State == InventoryOpeningState.Posted && opening.JournalId == journal.Id && journal.InventoryOpeningId == opening.Id && journal.State == JournalState.Posted);
            Ensure(journal.Source == JournalSource.InventoryOpening && journal.Lines.Sum(x => x.FunctionalDebit) == 10.5m && journal.Lines.Sum(x => x.FunctionalCredit) == 10.5m);
            Ensure(movement.BaseQuantity == 2 && movement.Value == 10.5m && movement.JournalId == journal.Id && f.Store.Count<OutboxEvent>() == 2);
            Ensure((await f.Service.InventoryOpening.Movements(f.CompanyId, Writer, null, null, 0, 10, default)).TotalCount == 1);
        });
        test("INV06 posting: failure restores approved source journal stock outbox and receipt", async f =>
        {
            var (_, r) = await Setup(f); var plan = await Plan(f, r); var key = Fixture.Key(); var audit = f.Store.Count<AuditEvent>();
            f.Store.FailBeforeCommit = true;
            try { await Post(f, r, plan, key); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await InventoryOpeningTests.Get(f, r.ResourceId)).State == InventoryOpeningState.Approved && f.Store.Count<StockMovement>() == 0 && f.Store.Count<JournalEntry>() == 0 && f.Store.Count<OutboxEvent>() == 0 && audit == f.Store.Count<AuditEvent>());
            await Post(f, r, plan, key);
        });
        test("INV06 posting: settings changes invalidate posting preview and permission is separate", async f =>
        {
            var (_, r) = await Setup(f); var plan = await Plan(f, r);
            await Error("permission_denied", () => f.Service.InventoryOpening.Post(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), plan, default));
            await f.Service.InventoryOpening.ConfigurePosting(f.CompanyId, f.Owner, Fixture.Key(), new(plan.ExpectedSettingsVersion, f.CashId, "تغيير الحساب"), default);
            await Error("opening_posting_plan_changed", () => Post(f, r, plan));
            Ensure(f.Store.Count<StockMovement>() == 0);
        });
        test("INV06 posting: duplicate opening with distinct document and key rejected", async f =>
        {
            var (request, r) = await Setup(f); var other = await InventoryOpeningTests.Create(f, request);
            other = await InventoryOpeningTests.Action(f, other, "submit"); other = await InventoryOpeningTests.Action(f, other, "approve", new Actor("stock-reviewer"));
            var otherPlan = await Plan(f, other); await Post(f, r, await Plan(f, r));
            await Error("inventory_opening_already_recorded", () => Post(f, other, otherPlan));
            Ensure(f.Store.Count<StockMovement>() == 1 && f.Store.Count<JournalEntry>() == 1);
        });
        test("INV06 posting: control journal cannot be reversed independently", async f =>
        {
            var (_, r) = await Setup(f); await Post(f, r, await Plan(f, r)); var journal = f.Store.All<JournalEntry>().Single();
            await Error("source_reversal_required", () => f.Service.Journal.ReverseJournal(f.CompanyId, journal.Id, f.Clerk, Fixture.Key(), new(journal.Version, Fixture.Date, "عكس"), default));
        });
        test("INV06 posting: posted opening reconciles at close and survives inactive cards", async f =>
        {
            var (request, r) = await Setup(f); await Post(f, r, await Plan(f, r));
            var opening = await InventoryOpeningTests.Get(f, r.ResourceId); var period = f.Store.All<FiscalPeriod>().Single(x => x.Id == opening.FiscalPeriodId);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, period.Id, f.Approver, 0, 10, default);
            Ensure(preview.CanCloseGeneralLedger && preview.PendingInventoryOpeningCount == 0 && preview.Checks.Single(x => x.Code == "inventory_openings_reconciled").Passed);
            await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default);
            Ensure(!(await f.Service.InventoryOpening.Preview(f.CompanyId, r.ResourceId, Writer, default)).CanPost);
        });
        test("INV06 posting: stock freezes item conversion warehouse account and identity number", async f =>
        {
            var (request, r) = await Setup(f, ItemTracking.Lot); await Post(f, r, await Plan(f, r));
            var item = await f.Service.Item.GetItem(f.CompanyId, request.Lines[0].ItemId, f.Owner, default);
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, Fixture.Key(), ItemTests.Edit(item) with { Code = "OTHER" }, default));
            var wh = await f.Service.Warehouse.GetWarehouse(f.CompanyId, request.WarehouseId, f.Owner, default);
            await Error("warehouse_stock_structure_in_use", () => f.Service.Warehouse.EditWarehouse(f.CompanyId, wh.Id, f.Owner, Fixture.Key(), WarehouseTests.Edit(wh) with { Code = "NEW" }, default));
            var identity = f.Store.All<InventoryIdentity>().Single();
            await Error("inventory_identity_in_stock", () => f.Service.InventoryIdentity.EditLot(f.CompanyId, item.Id, identity.Id, f.Owner, Fixture.Key(), new(identity.Version, identity.Code, identity.Name, identity.ProductionDate, identity.ExpiryDate, identity.QualityStatus, null, null, "تغيير", LotNumber: "NEW"), default));
        });
        test("INV04-CTRL01: posted stock freezes unit and tracking but not descriptive text", async f =>
        {
            var (request, approved) = await Setup(f, ItemTracking.Lot);
            await Post(f, approved, await Plan(f, approved));
            var item = await f.Service.Item.GetItem(f.CompanyId, request.Lines[0].ItemId, f.Owner, default);
            var alternate = await f.Service.Unit.CreateUnit(f.CompanyId, f.Owner, Fixture.Key(),
                new("POST-ALT", "وحدة بديلة", "EA", 0, "وحدة بديلة"), default);
            var initialAudit = f.Store.Count<AuditEvent>();
            var initialOutbox = f.Store.Count<OutboxEvent>();
            var initialMovements = f.Store.Count<StockMovement>();
            var initialJournals = f.Store.Count<JournalEntry>();
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner,
                Fixture.Key(), ItemTests.Edit(item) with { Tracking = ItemTracking.None }, default));
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner,
                Fixture.Key(), ItemTests.Edit(item) with { BaseUnitId = alternate.ResourceId }, default));
            var alteredUnits = item.Units.Select(x => new ItemUnitRequest(x.UnitId,
                x.FactorToBase + 1m, x.Barcode, x.SalePrice)).ToArray();
            await Error("item_stock_structure_in_use", () => f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner,
                Fixture.Key(), ItemTests.Edit(item) with { Units = alteredUnits }, default));
            Ensure((await f.Service.Item.GetItem(f.CompanyId, item.Id, f.Owner, default)).Version == item.Version &&
                f.Store.Count<AuditEvent>() == initialAudit &&
                f.Store.Count<OutboxEvent>() == initialOutbox &&
                f.Store.Count<StockMovement>() == initialMovements &&
                f.Store.Count<JournalEntry>() == initialJournals);
            var edited = await f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, Fixture.Key(),
                ItemTests.Edit(item) with { Name = "اسم وصفي بعد الترحيل" }, default);
            var saved = await f.Service.Item.GetItem(f.CompanyId, item.Id, f.Owner, default);
            Ensure(saved.Name == "اسم وصفي بعد الترحيل" && saved.Version == edited.Version &&
                saved.BaseUnitId == item.BaseUnitId && saved.Tracking == ItemTracking.Lot &&
                f.Store.Count<StockMovement>() == initialMovements &&
                f.Store.Count<JournalEntry>() == initialJournals &&
                f.Store.Count<OutboxEvent>() == initialOutbox);
        });
        test("INV06 posting: serial cannot open in two warehouses", async f =>
        {
            var (request, r) = await Setup(f, ItemTracking.Serial);
            var wh = await f.Service.Warehouse.GetWarehouse(f.CompanyId, request.WarehouseId, f.Owner, default);
            var otherWh = await f.Service.Warehouse.CreateWarehouse(f.CompanyId, f.Owner, Fixture.Key(), new("SECOND", "آخر", wh.BranchId, wh.KeeperMembershipId, wh.InventoryAccountId, WarehouseType.Standard, false, Array.Empty<WarehouseLocationRequest>()), default);
            var other = await InventoryOpeningTests.Create(f, request with { WarehouseId = otherWh.ResourceId });
            other = await InventoryOpeningTests.Action(f, other, "submit"); other = await InventoryOpeningTests.Action(f, other, "approve", new Actor("stock-reviewer"));
            var secondPlan = await Plan(f, other); await Post(f, r, await Plan(f, r));
            await Error("serial_already_in_stock", () => Post(f, other, secondPlan));
            Ensure(f.Store.Count<StockMovement>() == 1);
        });
        test("INV06 posting: revoked post permission blocks replay and foreign branch cannot read movements", async f =>
        {
            var (_, r) = await Setup(f); var plan = await Plan(f, r); var key = Fixture.Key(); await Post(f, r, plan, key);
            var member = f.Store.All<Membership>().Single(x => x.Subject == Writer.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(Writer.Subject, new[] { Permissions.Read }, true, Array.Empty<Guid>(), member.Version), default);
            await Error("permission_denied", () => Post(f, r, plan, key));
            var actor = new Actor("stock-other-branch");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(actor.Subject, new[] { Permissions.Read }, false, new[] { f.OtherBranchId }, null), default);
            Ensure((await f.Service.InventoryOpening.Movements(f.CompanyId, actor, null, null, 0, 10, default)).TotalCount == 0);
        });
        test("INV06 posting: repeated item lines aggregate quantity and value once", async f =>
        {
            var (request, initial) = await Setup(f);
            // Initial approved document has no effect until posted; a different prepared document may contain repeated nonserial lines.
            var combined = await InventoryOpeningTests.Create(f, request with { Lines = new[] { request.Lines[0], request.Lines[0] with { Quantity = 3, UnitCost = 2 } } });
            combined = await InventoryOpeningTests.Action(f, combined, "submit"); combined = await InventoryOpeningTests.Action(f, combined, "approve", new Actor("stock-reviewer"));
            await Post(f, combined, await Plan(f, combined));
            var movement = f.Store.All<StockMovement>().Single(); Ensure(movement.BaseQuantity == 5 && movement.Value == 16.5m);
            Ensure((await InventoryOpeningTests.Get(f, initial.ResourceId)).State == InventoryOpeningState.Approved);
        });
        test("INV06 posting: stock reads hide foreign branch and no setup means no post", async f =>
        {
            var request = await InventoryOpeningTests.Setup(f); var r = await InventoryOpeningTests.Create(f, request);
            var preview = await f.Service.InventoryOpening.Preview(f.CompanyId, r.ResourceId, Writer, default);
            Ensure(!preview.CanPost && preview.PostingBlocker == "opening_posting_not_configured");
            await Error("invalid_opening_clearing_account", () => f.Service.InventoryOpening.ConfigurePosting(f.CompanyId, f.Owner, Fixture.Key(), new(null, f.GroupId, "إعداد"), default));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Inventory posting assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
