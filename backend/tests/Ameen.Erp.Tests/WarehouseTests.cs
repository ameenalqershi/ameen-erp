using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class WarehouseTests
{
    public static async Task<CreateWarehouseRequest> Setup(Fixture f)
    {
        var account = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("WH-STOCK", "مخزون", AccountType.Asset, f.GroupId, true, true, false, null, NormalBalance.Debit), default);
        var keeper = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
        return new("001", "مخزن", f.BranchId, keeper.Id, account.ResourceId, WarehouseType.Standard, true,
            new[] { new WarehouseLocationRequest("001", "رف أول") }, "Warehouse", "ملاحظات");
    }
    public static EditWarehouseRequest Edit(Warehouse w) => new(w.Version, w.Code, w.Name, w.BranchId, w.KeeperMembershipId, w.InventoryAccountId,
        w.Type, w.TrackLocations, w.Locations.Select(x => new WarehouseLocationRequest(x.Code, x.Description)).ToArray(), w.EnglishName, w.Notes, "تعديل");
    private static Task<CommandReceipt> Create(Fixture f, CreateWarehouseRequest r) => f.Service.Warehouse.CreateWarehouse(f.CompanyId, f.Owner, Fixture.Key(), r, default);
    private static Task<Warehouse> Get(Fixture f, Guid id) => f.Service.Warehouse.GetWarehouse(f.CompanyId, id, f.Owner, default);
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV01-FG01: warehouse generated header tenant code and mandatory Arabic name", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request with { Code = " wh-001 ", Name = "  مخزن تجريبي  " });
            var warehouse = await Get(f, created.ResourceId);
            Ensure(warehouse.Id == created.ResourceId && warehouse.Id != Guid.Empty &&
                warehouse.CompanyId == f.CompanyId && warehouse.Code == "WH-001" && warehouse.Name == "مخزن تجريبي");
            await Error("invalid_text", () => Create(f, request with { Code = "", Name = "مخزن" }));
            await Error("invalid_text", () => Create(f, request with { Code = "CODE", Name = " " }));
            await Error("invalid_code", () => Create(f, request with { Code = "bad code" }));
            await Error("duplicate_warehouse", () => Create(f, request with { Code = "wh-001" }));
            Ensure((await f.Service.Warehouse.SearchWarehouses(f.CompanyId, f.Limited,
                "WH-001", f.BranchId, true, 0, 10, default)).TotalCount == 1);
            Ensure(f.Store.Count<Warehouse>() == 1 && f.Store.Count<JournalEntry>() == 0);
        });
        test("INV01-FG02: optional bilingual metadata, active state and required branch", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request with { EnglishName = null, Notes = null });
            var warehouse = await Get(f, created.ResourceId);
            Ensure(warehouse.EnglishName is null && warehouse.Notes is null &&
                warehouse.BranchId == f.BranchId && warehouse.IsActive);
            await Error("reference_not_found", () => Create(f, request with { Code = "OTHER", BranchId = Guid.NewGuid() }));
            var updated = await f.Service.Warehouse.EditWarehouse(f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(),
                Edit(warehouse) with { EnglishName = "Regional stock", Notes = "Warehouse note" }, default);
            warehouse = await Get(f, warehouse.Id);
            Ensure(warehouse.EnglishName == "Regional stock" && warehouse.Notes == "Warehouse note");
            await f.Service.Warehouse.SetWarehouseStatus(f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(),
                new(updated.Version, false, "تعطيل"), default);
            Ensure(!(await Get(f, warehouse.Id)).IsActive &&
                (await f.Service.Warehouse.SearchWarehouses(f.CompanyId, f.Limited, null,
                    f.BranchId, false, 0, 10, default)).TotalCount == 1);
        });
        test("INV01-FG04: location codes and descriptions persist and replace atomically", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request with { Locations = new[]
            {
                new WarehouseLocationRequest("A01", "رف أ"),
                new WarehouseLocationRequest("B02", "رف ب")
            }});
            var warehouse = await Get(f, created.ResourceId);
            Ensure(warehouse.Locations.Count == 2 && warehouse.Locations.Any(x => x.Code == "A01" && x.Description == "رف أ") &&
                warehouse.Locations.Any(x => x.Code == "B02" && x.Description == "رف ب"));
            var oldIds = warehouse.Locations.Select(x => x.Id).ToHashSet();
            var updated = await f.Service.Warehouse.EditWarehouse(f.CompanyId, warehouse.Id, f.Owner, Fixture.Key(),
                Edit(warehouse) with { Locations = new[] { new WarehouseLocationRequest("C03", "رف ج") } }, default);
            warehouse = await Get(f, warehouse.Id);
            Ensure(warehouse.Version == updated.Version && warehouse.Revision == 2 &&
                warehouse.Locations.Count == 1 && warehouse.Locations[0].Code == "C03" &&
                !oldIds.Contains(warehouse.Locations[0].Id));
            await Error("duplicate_location", () => f.Service.Warehouse.EditWarehouse(f.CompanyId,
                warehouse.Id, f.Owner, Fixture.Key(), Edit(warehouse) with
                { Locations = new[] { new WarehouseLocationRequest("Z1", "أ"), new WarehouseLocationRequest("z1", "ب") } }, default));
            Ensure((await Get(f, warehouse.Id)).Version == updated.Version);
        });
        test("INV01-FG05: creation/modification actor time and integer revisions audit correctly", async f =>
        {
            var created = await Create(f, await Setup(f));
            var warehouse = await Get(f, created.ResourceId);
            var first = await f.Service.Warehouse.ListWarehouseAudit(f.CompanyId, warehouse.Id, f.Owner, 0, 10, default);
            Ensure(first.TotalCount == 1 && first.Items[0].Action == "warehouse.created" &&
                first.Items[0].ActorId == f.Owner.Subject && first.Items[0].OccurredAt != default);
            using (var json = System.Text.Json.JsonDocument.Parse(first.Items[0].Details))
                Ensure(json.RootElement.GetProperty("after").GetProperty("revision").GetInt32() == 1);
            await Error("version_conflict", () => f.Service.Warehouse.EditWarehouse(f.CompanyId, warehouse.Id,
                f.Owner, Fixture.Key(), Edit(warehouse) with { ExpectedVersion = Guid.NewGuid() }, default));
            Ensure((await f.Service.Warehouse.ListWarehouseAudit(f.CompanyId, warehouse.Id, f.Owner, 0, 10, default)).TotalCount == 1);
            var updated = await f.Service.Warehouse.EditWarehouse(f.CompanyId, warehouse.Id, f.Owner,
                Fixture.Key(), Edit(warehouse) with { Name = "تعديل الاسم" }, default);
            var second = await f.Service.Warehouse.ListWarehouseAudit(f.CompanyId, warehouse.Id, f.Owner, 0, 10, default);
            Ensure(updated.Version != created.Version && (await Get(f, warehouse.Id)).Revision == 2 &&
                second.TotalCount == 2 && second.Items.All(x => x.ActorId == f.Owner.Subject && x.OccurredAt != default));
            using var detail = System.Text.Json.JsonDocument.Parse(second.Items.Single(x => x.Action == "warehouse.edited").Details);
            Ensure(detail.RootElement.GetProperty("before").GetProperty("revision").GetInt32() == 1 &&
                detail.RootElement.GetProperty("after").GetProperty("revision").GetInt32() == 2);
            await Error("permission_denied", () => f.Service.Warehouse.ListWarehouseAudit(f.CompanyId,
                warehouse.Id, f.Limited, 0, 10, default));
        });
        test("INV01: warehouse locations bilingual card and revision roundtrip without financial effects", async f =>
        {
            var r = await Create(f, await Setup(f)); var w = await Get(f, r.ResourceId);
            Ensure(w.Code == "001" && w.Locations.Single().Code == "001" && w.Revision == 1 && w.EnglishName == "Warehouse");
            Ensure((await f.Service.Warehouse.SearchWarehouses(f.CompanyId, f.Limited, "Warehouse", null, true, 0, 10, default)).Items.Single().Id == w.Id);
            Ensure((await f.Service.Warehouse.ListWarehouseAudit(f.CompanyId, w.Id, f.Owner, 0, 10, default)).TotalCount == 1 && f.Store.Count<JournalEntry>() == 0);
        });
        test("INV01: location policy rejects missing duplicated or unsolicited locations", async f =>
        {
            var r = await Setup(f);
            await Error("invalid_warehouse_locations", () => Create(f, r with { Locations = Array.Empty<WarehouseLocationRequest>() }));
            await Error("invalid_warehouse_locations", () => Create(f, r with { TrackLocations = false }));
            await Error("duplicate_location", () => Create(f, r with { Locations = new[] { r.Locations[0], r.Locations[0] with { Code = " 001 " } } }));
            await Create(f, r with { TrackLocations = false, Locations = Array.Empty<WarehouseLocationRequest>() });
        });
        test("INV01: branch-scoped readers cannot disclose another branch warehouse or counts", async f =>
        {
            var r = await Setup(f); var owner = f.Store.All<Membership>().Single(x => x.Subject == f.Owner.Subject);
            var w = await Create(f, r with { BranchId = f.OtherBranchId, KeeperMembershipId = owner.Id });
            Ensure((await f.Service.Warehouse.SearchWarehouses(f.CompanyId, f.Limited, null, null, null, 0, 10, default)).TotalCount == 0);
            await Error("branch_access_denied", () => f.Service.Warehouse.GetWarehouse(f.CompanyId, w.ResourceId, f.Limited, default));
            await Error("branch_access_denied", () => f.Service.Warehouse.SearchWarehouses(f.CompanyId, f.Limited, null, f.OtherBranchId, null, 0, 10, default));
            await Error("permission_denied", () => f.Service.Warehouse.CreateWarehouse(f.CompanyId, f.Limited, Fixture.Key(), r, default));
        });
        test("INV01: keeper must be active and authorized for warehouse branch", async f =>
        {
            var r = await Setup(f);
            await Error("invalid_warehouse_keeper", () => Create(f, r with { BranchId = f.OtherBranchId }));
            var m = f.Store.All<Membership>().Single(x => x.Id == r.KeeperMembershipId);
            await f.Service.Membership.SetMembershipStatus(f.CompanyId, m.Id, f.Owner, Fixture.Key(), new(m.Version, false, "إيقاف"), default);
            await Error("invalid_warehouse_keeper", () => Create(f, r));
        });
        test("INV01: only active posting asset control accounts are accepted", async f =>
        {
            var r = await Setup(f);
            await Error("invalid_warehouse_account", () => Create(f, r with { InventoryAccountId = f.CashId }));
            await Error("invalid_warehouse_account", () => Create(f, r with { InventoryAccountId = f.GroupId }));
            var a = f.Store.All<Account>().Single(x => x.Id == r.InventoryAccountId);
            await f.Service.Account.SetAccountStatus(f.CompanyId, a.Id, f.Owner, Fixture.Key(), new(a.Version, false, "إيقاف"), default);
            await Error("invalid_warehouse_account", () => Create(f, r));
        });
        test("INV01: warehouse reference locks account ancestors and branch structure even when disabled", async f =>
        {
            var r = await Setup(f); var w = await Create(f, r);
            var a = f.Store.All<Account>().Single(x => x.Id == r.InventoryAccountId); var b = f.Store.All<Branch>().Single(x => x.Id == r.BranchId);
            await Error("account_used_by_active_warehouse", () => f.Service.Account.SetAccountStatus(f.CompanyId, a.Id, f.Owner, Fixture.Key(), new(a.Version, false, "إيقاف"), default));
            await Error("branch_used_by_active_warehouse", () => f.Service.Branch.SetBranchStatus(f.CompanyId, b.Id, f.Owner, Fixture.Key(), new(b.Version, false, "إيقاف"), default));
            Ensure((await f.Service.Account.GetAccountUsage(f.CompanyId, f.GroupId, f.Owner, 0, 10, default)).HasUsageInSubtree);
            await f.Service.Warehouse.SetWarehouseStatus(f.CompanyId, w.ResourceId, f.Owner, Fixture.Key(), new(w.Version, false, "إيقاف"), default);
            Ensure((await f.Service.Warehouse.ListAccountWarehouses(f.CompanyId, a.Id, f.Owner, 0, 10, default)).TotalCount == 1);
            Ensure((await f.Service.Branch.GetBranchUsage(f.CompanyId, b.Id, f.Owner, default)).HasSubtreeFinancialUsage);
        });
        test("INV01: keeper revocation remains possible and blocks warehouse reactivation", async f =>
        {
            var r = await Setup(f); var w = await Create(f, r); var m = f.Store.All<Membership>().Single(x => x.Id == r.KeeperMembershipId);
            await f.Service.Membership.SetMembershipStatus(f.CompanyId, m.Id, f.Owner, Fixture.Key(), new(m.Version, false, "سحب صلاحية"), default);
            var disabled = await f.Service.Warehouse.SetWarehouseStatus(f.CompanyId, w.ResourceId, f.Owner, Fixture.Key(), new(w.Version, false, "إيقاف"), default);
            await Error("invalid_warehouse_keeper", () => f.Service.Warehouse.SetWarehouseStatus(f.CompanyId, w.ResourceId, f.Owner, Fixture.Key(), new(disabled.Version, true, "تفعيل"), default));
        });
        test("INV01: failed location replacement rolls back and retry replays with stale version rejected", async f =>
        {
            var r = await Create(f, await Setup(f)); var w = await Get(f, r.ResourceId); var oldLocation = w.Locations.Single().Id;
            var edit = Edit(w) with { Type = WarehouseType.Quarantine, Locations = new[] { new WarehouseLocationRequest("002", "موقع عزل") } }; var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.Warehouse.EditWarehouse(f.CompanyId, w.Id, f.Owner, key, edit, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await Get(f, w.Id)).Locations.Single().Id == oldLocation && (await Get(f, w.Id)).Version == r.Version);
            var changed = await f.Service.Warehouse.EditWarehouse(f.CompanyId, w.Id, f.Owner, key, edit, default);
            Ensure(await f.Service.Warehouse.EditWarehouse(f.CompanyId, w.Id, f.Owner, key, edit, default) == changed);
            Ensure((await Get(f, w.Id)).Type == WarehouseType.Quarantine && (await Get(f, w.Id)).Revision == 2);
            await Error("version_conflict", () => f.Service.Warehouse.EditWarehouse(f.CompanyId, w.Id, f.Owner, Fixture.Key(), edit, default));
        });
        test("INV01: duplicate company code invalid enum and foreign references rejected", async f =>
        {
            var r = await Setup(f); await Create(f, r);
            await Error("duplicate_warehouse", () => Create(f, r));
            await Error("invalid_command_payload", () => Create(f, r with { Type = (WarehouseType)99 }));
            await Error("reference_not_found", () => Create(f, r with { BranchId = Guid.NewGuid() }));
            await Error("reference_not_found", () => Create(f, r with { KeeperMembershipId = Guid.NewGuid() }));
            await Error("reference_not_found", () => Create(f, r with { InventoryAccountId = Guid.NewGuid() }));
        });
        test("INV01: disabled branch rejected and paging and audit permissions enforced", async f =>
        {
            var r = await Setup(f); var b = f.Store.All<Branch>().Single(x => x.Id == r.BranchId);
            await f.Service.Branch.SetBranchStatus(f.CompanyId, b.Id, f.Owner, Fixture.Key(), new(b.Version, false, "إيقاف"), default);
            await Error("invalid_warehouse_branch", () => Create(f, r));
            await Error("invalid_pagination", () => f.Service.Warehouse.SearchWarehouses(f.CompanyId, f.Owner, null, null, null, 0, 201, default));
            await Error("permission_denied", () => f.Service.Warehouse.ListWarehouseAudit(f.CompanyId, Guid.NewGuid(), f.Limited, 0, 10, default));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Warehouse assertion failed"); }
    private static async Task Error(string code, Func<Task> action) { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
