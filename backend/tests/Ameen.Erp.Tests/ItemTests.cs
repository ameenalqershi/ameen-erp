using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class ItemTests
{
    public static async Task<CreateItemRequest> Setup(Fixture f)
    {
        var sales = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("ITEM-S", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default);
        var stock = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("ITEM-I", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default);
        var cost = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("ITEM-C", "تكلفة", AccountType.Expense, null, true, false, false, null, NormalBalance.Debit), default);
        var group = await f.Service.ItemGroup.CreateItemGroup(f.CompanyId, f.Owner, Fixture.Key(), new("ITEM-G", "مجموعة", sales.ResourceId, InventoryAccountId: stock.ResourceId, CostAccountId: cost.ResourceId), default);
        var each = await f.Service.Unit.CreateUnit(f.CompanyId, f.Owner, Fixture.Key(), new("EA", "حبة", "EA", 0, "حبة"), default);
        var box = await f.Service.Unit.CreateUnit(f.CompanyId, f.Owner, Fixture.Key(), new("BOX", "كرتون", "BOX", 2, "كرتون"), default);
        return new("001", "صنف", ItemType.Goods, ItemTracking.None, group.ResourceId, each.ResourceId,
            new[] { new ItemUnitRequest(each.ResourceId, 1, "00001", 5m), new ItemUnitRequest(box.ResourceId, 12, "00012", 60m) }, EnglishName: "Item");
    }
    public static EditItemRequest Edit(Item x) => new(x.Version, x.Code, x.Name, x.Type, x.Tracking, x.GroupId, x.BaseUnitId,
        x.Units.Select(u => new ItemUnitRequest(u.UnitId, u.FactorToBase, u.Barcode, u.SalePrice)).ToArray(), x.ShelfLifeDays, x.ReorderLevel, x.EnglishName, x.Notes, "تحديث");
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV04: item units barcode and price roundtrip without stock movement", async f =>
        {
            var request = await Setup(f); var r = await Create(f, request); var item = await Get(f, r.ResourceId);
            Ensure(item.Revision == 1 && item.Units.Count == 2 && item.Units.Any(x => x.Barcode == "00001"));
            Ensure((await f.Service.Item.SearchItems(f.CompanyId, f.Limited, "00012", request.GroupId, true, 0, 10, default)).TotalCount == 1);
            Ensure(f.Store.Count<JournalEntry>() == 0);
            var audit = await f.Service.Item.ListItemAudit(f.CompanyId, item.Id, f.Owner, 0, 10, default);
            Ensure(audit.Items.Single().Details.Contains("functionalCurrency", StringComparison.Ordinal) && audit.Items.Single().Details.Contains("SAR", StringComparison.Ordinal));
        });
        test("INV04: per-item conversion is exact and rejects fractional base quantities", async f =>
        {
            var request = await Setup(f); var r = await Create(f, request); var box = request.Units[1].UnitId;
            Ensure((await f.Service.Item.ConvertItemQuantity(f.CompanyId, r.ResourceId, f.Clerk, box, 0.5m, default)).BaseQuantity == 6m);
            await Error("conversion_precision", () => f.Service.Item.ConvertItemQuantity(f.CompanyId, r.ResourceId, f.Clerk, box, 0.01m, default));
            await Error("quantity_precision", () => f.Service.Item.ConvertItemQuantity(f.CompanyId, r.ResourceId, f.Clerk, request.BaseUnitId, 0.5m, default));
            await Error("quantity_overflow", () => f.Service.Item.ConvertItemQuantity(f.CompanyId, r.ResourceId, f.Clerk, box, 99_999_999_999_999m, default));
        });
        test("INV04: services cannot carry stock policy and serial base units cannot allow fractions", async f =>
        {
            var r = await Setup(f);
            await Error("service_has_no_stock", () => Create(f, r with { Type = ItemType.Service, Tracking = ItemTracking.Lot }));
            await Error("shelf_life_requires_tracking", () => Create(f, r with { ShelfLifeDays = 30 }));
            await Error("serial_unit_fraction", () => Create(f, r with { Tracking = ItemTracking.Serial, BaseUnitId = r.Units[1].UnitId }));
            await Create(f, r with { Type = ItemType.Service });
        });
        test("INV04: missing base unit repeated units invalid factors and price precision rejected", async f =>
        {
            var r = await Setup(f);
            await Error("base_unit_factor", () => Create(f, r with { Units = new[] { r.Units[1] } }));
            await Error("duplicate_item_unit", () => Create(f, r with { Units = new[] { r.Units[0] with { Barcode = null }, r.Units[0] } }));
            await Error("invalid_conversion_factor", () => Create(f, r with { Units = new[] { r.Units[0] with { FactorToBase = 0 } } }));
            await Error("price_precision", () => Create(f, r with { Units = new[] { r.Units[0] with { SalePrice = 0.001m } } }));
        });
        test("INV04: company barcode uniqueness includes disabled items", async f =>
        {
            var request = await Setup(f); var r = await Create(f, request);
            await f.Service.Item.SetItemStatus(f.CompanyId, r.ResourceId, f.Owner, Fixture.Key(), new(r.Version, false, "إيقاف"), default);
            await Error("duplicate_barcode", () => Create(f, request with { Code = "002" }));
        });
        test("INV04 INV03: used unit structure locks and active item prevents disabling", async f =>
        {
            var request = await Setup(f); await Create(f, request); var unit = await f.Service.Unit.GetUnit(f.CompanyId, request.BaseUnitId, f.Owner, default);
            await Error("unit_structure_in_use", () => f.Service.Unit.EditUnit(f.CompanyId, unit.Id, f.Owner, Fixture.Key(), UnitTests.Edit(unit) with { QuantityScale = 1 }, default));
            await Error("unit_used_by_active_item", () => f.Service.Unit.SetUnitStatus(f.CompanyId, unit.Id, f.Owner, Fixture.Key(), new(unit.Version, false, "إيقاف"), default));
            Ensure((await f.Service.Unit.ListUnitItems(f.CompanyId, unit.Id, f.Owner, 0, 10, default)).TotalCount == 1);
        });
        test("INV04 INV02: used group structural edits and disabling are protected", async f =>
        {
            var request = await Setup(f); await Create(f, request); var group = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, request.GroupId, f.Owner, default);
            await Error("item_group_in_use", () => f.Service.ItemGroup.EditItemGroup(f.CompanyId, group.Id, f.Owner, Fixture.Key(), ItemGroupTests.Edit(group) with { Code = "NEW" }, default));
            await Error("group_used_by_active_item", () => f.Service.ItemGroup.SetItemGroupStatus(f.CompanyId, group.Id, f.Owner, Fixture.Key(), new(group.Version, false, "إيقاف"), default));
        });
        test("INV04: item prices freeze functional currency before journals", async f =>
        {
            await Create(f, await Setup(f)); await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            var c = await f.Service.Company.GetCompany(f.CompanyId, f.Owner, default);
            await Error("functional_currency_in_use", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), CompanyTests.Edit(c) with { FunctionalCurrency = "USD" }, default));
            Ensure(!(await f.Service.Company.GetCompanyUsage(f.CompanyId, f.Owner, default)).CanChangeFunctionalCurrency);
        });
        test("INV04: failed line replacement is atomic and exact retry replays", async f =>
        {
            var r = await Create(f, await Setup(f)); var item = await Get(f, r.ResourceId); var request = Edit(item) with { Name = "معدل" }; var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, key, request, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await Get(f, item.Id)).Version == r.Version && (await Get(f, item.Id)).Units.Count == 2);
            var changed = await f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, key, request, default);
            Ensure(await f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, key, request, default) == changed);
            await Error("version_conflict", () => f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, Fixture.Key(), request, default));
        });
        test("INV04: company scope and write permissions are mandatory", async f =>
        {
            var request = await Setup(f);
            await Error("permission_denied", () => f.Service.Item.CreateItem(f.CompanyId, f.Limited, Fixture.Key(), request, default));
            await Error("reference_not_found", () => Create(f, request with { GroupId = Guid.NewGuid() }));
            await Error("reference_not_found", () => Create(f, request with { BaseUnitId = Guid.NewGuid() }));
            await Error("reference_not_found", () => Create(f, request with { Units = new[] { request.Units[0], request.Units[1] with { UnitId = Guid.NewGuid() } } }));
        });
    }
    private static Task<CommandReceipt> Create(Fixture f, CreateItemRequest r) => f.Service.Item.CreateItem(f.CompanyId, f.Owner, Fixture.Key(), r, default);
    private static Task<Item> Get(Fixture f, Guid id) => f.Service.Item.GetItem(f.CompanyId, id, f.Owner, default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Item assertion failed"); }
    private static async Task Error(string code, Func<Task> action) { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
