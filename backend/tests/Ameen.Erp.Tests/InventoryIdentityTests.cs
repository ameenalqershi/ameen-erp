using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;

public static class InventoryIdentityTests
{
    private static CreateInventoryIdentityRequest Request => new("LOT-01", "دفعة", new(2026, 1, 1), new(2027, 1, 1), InventoryQualityStatus.Quarantine);
    private static async Task<Guid> Setup(Fixture f, ItemTracking tracking = ItemTracking.Lot)
    {
        var request = await ItemTests.Setup(f);
        return (await f.Service.Item.CreateItem(f.CompanyId, f.Owner, Fixture.Key(), request with { Tracking = tracking, ShelfLifeDays = 30 }, default)).ResourceId;
    }
    private static Task<CommandReceipt> Create(Fixture f, Guid item, CreateInventoryIdentityRequest? request = null, string? key = null) =>
        f.Service.InventoryIdentity.CreateLot(f.CompanyId, item, f.Owner, key ?? Fixture.Key(), request ?? Request, default);
    private static Task<InventoryIdentity> Get(Fixture f, Guid item, Guid id) => f.Service.InventoryIdentity.GetLot(f.CompanyId, item, id, f.Owner, default);
    private static EditInventoryIdentityRequest Edit(InventoryIdentity x) => new(x.Version, x.Code, x.Name, x.ProductionDate, x.ExpiryDate, x.QualityStatus, x.EnglishName, x.Notes, "تصحيح");
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV05: independent lot number uniqueness search and omitted edit preservation", async f =>
        {
            var item = await Setup(f); var request = Request with { Code = "CARD-1", LotNumber = "BATCH-900" };
            var receipt = await Create(f, item, request); var lot = await Get(f, item, receipt.ResourceId);
            Ensure(lot.Code == "CARD-1" && lot.LotNumber == "BATCH-900" && lot.SerialNumber is null);
            Ensure((await f.Service.InventoryIdentity.SearchLots(f.CompanyId, item, f.Owner, "BATCH-900", null, null, 0, 10, default)).TotalCount == 1);
            await Error("duplicate_inventory_identity", () => Create(f, item, request with { Code = "CARD-2" }));
            await f.Service.InventoryIdentity.EditLot(f.CompanyId, item, lot.Id, f.Owner, Fixture.Key(), Edit(lot) with { Code = "CARD-NEW" }, default);
            lot = await Get(f, item, lot.Id); Ensure(lot.Code == "CARD-NEW" && lot.LotNumber == "BATCH-900");
            await f.Service.InventoryIdentity.SetLotStatus(f.CompanyId, item, lot.Id, f.Owner, Fixture.Key(), new(lot.Version, false, "إيقاف"), default);
            await Error("duplicate_inventory_identity", () => Create(f, item, request with { Code = "CARD-2" }));
        });
        test("INV05: serial number independent from card and opposite number rejected", async f =>
        {
            var item = await Setup(f, ItemTracking.Serial);
            var r = await f.Service.InventoryIdentity.CreateSerial(f.CompanyId, item, f.Owner, Fixture.Key(), Request with { Code = "CARD-S", SerialNumber = "SN-001" }, default);
            var serial = await f.Service.InventoryIdentity.GetSerial(f.CompanyId, item, r.ResourceId, f.Owner, default);
            Ensure(serial.SerialNumber == "SN-001" && serial.LotNumber is null && serial.Code == "CARD-S");
            await Error("inventory_number_tracking_mismatch", () => f.Service.InventoryIdentity.CreateSerial(f.CompanyId, item, f.Owner, Fixture.Key(), Request with { LotNumber = "WRONG" }, default));
        });
        test("INV05: omitted number retains legacy fingerprint and explicit empty number rejected", async f =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Request, JsonDefaults.Options);
            Ensure(!json.Contains("lotNumber", StringComparison.Ordinal) && !json.Contains("serialNumber", StringComparison.Ordinal));
            var item = await Setup(f); var r = await Create(f, item); Ensure((await Get(f, item, r.ResourceId)).LotNumber == Request.Code);
            await Error("invalid_text", () => Create(f, item, Request with { Code = "OTHER", LotNumber = "" }));
        });
        test("INV05: lot roundtrip search audit and idempotency without stock movement", async f =>
        {
            var item = await Setup(f); var key = Fixture.Key(); var receipt = await Create(f, item, key: key);
            Ensure(await Create(f, item, key: key) == receipt);
            var lot = await Get(f, item, receipt.ResourceId); Ensure(lot.Revision == 1 && lot.QualityStatus == InventoryQualityStatus.Quarantine);
            Ensure((await f.Service.InventoryIdentity.SearchLots(f.CompanyId, item, f.Limited, "LOT", InventoryQualityStatus.Quarantine, true, 0, 10, default)).TotalCount == 1);
            Ensure((await f.Service.InventoryIdentity.ListLotAudit(f.CompanyId, item, lot.Id, f.Owner, 0, 10, default)).TotalCount == 1);
            Ensure(f.Store.Count<JournalEntry>() == 0 && f.Store.Count<OutboxEvent>() == 0);
        });
        test("INV05: expiry boundaries quality enum and item shelf life validated", async f =>
        {
            var item = await Setup(f);
            await Error("invalid_inventory_expiry", () => Create(f, item, Request with { ExpiryDate = Request.ProductionDate }));
            await Error("invalid_inventory_expiry", () => Create(f, item, Request with { ExpiryDate = new(2025, 1, 1) }));
            await Error("inventory_expiry_required", () => Create(f, item, Request with { ExpiryDate = null }));
            await Error("invalid_command_payload", () => Create(f, item, Request with { QualityStatus = (InventoryQualityStatus)99 }));
            Ensure(f.Store.Count<InventoryIdentity>() == 0);
        });
        test("INV05: disabled identity still reserves number and freezes item tracking", async f =>
        {
            var item = await Setup(f); var r = await Create(f, item);
            await f.Service.InventoryIdentity.SetLotStatus(f.CompanyId, item, r.ResourceId, f.Owner, Fixture.Key(), new(r.Version, false, "إيقاف"), default);
            await Error("duplicate_inventory_identity", () => Create(f, item));
            var card = await f.Service.Item.GetItem(f.CompanyId, item, f.Owner, default);
            await Error("item_tracking_in_use", () => f.Service.Item.EditItem(f.CompanyId, item, f.Owner, Fixture.Key(), ItemTests.Edit(card) with { Tracking = ItemTracking.Serial }, default));
        });
        test("INV05: edit failure rolls back audit and version then retry and stale edit", async f =>
        {
            var item = await Setup(f); var r = await Create(f, item); var request = Edit(await Get(f, item, r.ResourceId)) with { Name = "معدل" }; var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.InventoryIdentity.EditLot(f.CompanyId, item, r.ResourceId, f.Owner, key, request, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await Get(f, item, r.ResourceId)).Version == r.Version);
            var updated = await f.Service.InventoryIdentity.EditLot(f.CompanyId, item, r.ResourceId, f.Owner, key, request, default);
            Ensure(updated == await f.Service.InventoryIdentity.EditLot(f.CompanyId, item, r.ResourceId, f.Owner, key, request, default));
            await Error("version_conflict", () => f.Service.InventoryIdentity.EditLot(f.CompanyId, item, r.ResourceId, f.Owner, Fixture.Key(), request, default));
            Ensure((await Get(f, item, r.ResourceId)).Revision == 2);
        });
        test("INV05: serial and lot routes cannot cross and permissions precede writes", async f =>
        {
            var item = await Setup(f, ItemTracking.Serial);
            await Error("item_tracking_mismatch", () => Create(f, item));
            var r = await f.Service.InventoryIdentity.CreateSerial(f.CompanyId, item, f.Owner, Fixture.Key(), Request, default);
            await Error("reference_not_found", () => Get(f, item, r.ResourceId));
            await Error("permission_denied", () => f.Service.InventoryIdentity.CreateSerial(f.CompanyId, item, f.Limited, Fixture.Key(), Request, default));
            await Error("reference_not_found", () => f.Service.InventoryIdentity.GetSerial(f.CompanyId, Guid.NewGuid(), r.ResourceId, f.Owner, default));
            Ensure((await f.Service.InventoryIdentity.GetSerial(f.CompanyId, item, r.ResourceId, f.Owner, default)).Tracking == ItemTracking.Serial);
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Inventory identity assertion failed"); }
    private static async Task Error(string code, Func<Task> work)
    { try { await work(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
