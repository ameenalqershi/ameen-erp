using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class ItemGroupTests
{
    public static EditItemGroupRequest Edit(ItemGroup x) => new(x.Version, x.Code, x.Name, x.SalesAccountId, x.ParentId, x.InventoryAccountId, x.CostAccountId, x.EnglishName, x.Notes, "تحديث");
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV02: group hierarchy bilingual fields and accounting references roundtrip", async f =>
        {
            var sales = await Sales(f); var parent = await Create(f, sales, "ROOT");
            var r = await Create(f, sales, "001", parent.ResourceId);
            var group = await Get(f, r.ResourceId); Ensure(group.Code == "001" && group.Revision == 1 && group.ParentId == parent.ResourceId && group.EnglishName == "Group");
            Ensure((await f.Service.SearchItemGroups(f.CompanyId, f.Limited, "Group", parent.ResourceId, true, 0, 10, default)).TotalCount == 1);
            Ensure((await f.Service.ListItemGroupAudit(f.CompanyId, group.Id, f.Owner, 0, 10, default)).TotalCount == 1);
        });
        test("INV02: cycles and disabled parents rejected", async f =>
        {
            var sales = await Sales(f); var a = await Create(f, sales, "A"); var b = await Create(f, sales, "B", a.ResourceId);
            await Error("item_group_cycle", () => f.Service.EditItemGroup(f.CompanyId, a.ResourceId, f.Owner, Fixture.Key(), Edit(f.Store.All<ItemGroup>().Single(x => x.Id == a.ResourceId)) with { ParentId = b.ResourceId }, default));
            await Error("item_group_cycle", () => f.Service.EditItemGroup(f.CompanyId, b.ResourceId, f.Owner, Fixture.Key(), Edit(f.Store.All<ItemGroup>().Single(x => x.Id == b.ResourceId)) with { ParentId = b.ResourceId }, default));
            await Error("active_child_groups", () => Status(f, a.ResourceId, false));
            await Status(f, b.ResourceId, false); await Status(f, a.ResourceId, false);
            await Error("inactive_parent_group", () => Status(f, b.ResourceId, true));
        });
        test("INV02: wrong account classification and unprotected inventory account rejected", async f =>
        {
            await Error("invalid_group_account", () => Create(f, f.CashId, "BAD"));
            var sales = await Sales(f);
            await Error("invalid_group_account", () => f.Service.CreateItemGroup(f.CompanyId, f.Owner, Fixture.Key(), new("BAD", "مجموعة", sales, InventoryAccountId: f.CashId), default));
            var stock = await f.Service.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("STOCK", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default);
            var cost = await f.Service.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("COGS", "تكلفة", AccountType.Expense, null, true, false, false, null, NormalBalance.Debit), default);
            await f.Service.CreateItemGroup(f.CompanyId, f.Owner, Fixture.Key(), new("VALID", "مجموعة", sales, InventoryAccountId: stock.ResourceId, CostAccountId: cost.ResourceId), default);
        });
        test("INV02 GL01: group reference locks account structure and blocks disabling active dependency", async f =>
        {
            var sales = await Sales(f); var group = await Create(f, sales, "A"); var account = f.Store.All<Account>().Single(x => x.Id == sales);
            await Error("account_used_by_active_group", () => f.Service.SetAccountStatus(f.CompanyId, sales, f.Owner, Fixture.Key(), new(account.Version, false, "تعطيل"), default));
            account = f.Store.All<Account>().Single(x => x.Id == sales);
            await Error("account_structure_in_use", () => f.Service.EditAccount(f.CompanyId, sales, f.Owner, Fixture.Key(), new(account.Version, "NEW", account.Name, account.Type, account.ParentId, account.IsPosting, account.IsControl, account.RequiresCostCenter, account.CurrencyCode, account.NormalBalance, account.EnglishName, account.Notes, "تعديل"), default));
            Ensure((await f.Service.GetAccountUsage(f.CompanyId, sales, f.Owner, 0, 10, default)).HasUsageInSubtree);
            Ensure((await f.Service.ListAccountItemGroups(f.CompanyId, sales, f.Owner, 0, 10, default)).TotalCount == 1);
            await Status(f, group.ResourceId, false); account = f.Store.All<Account>().Single(x => x.Id == sales);
            await f.Service.SetAccountStatus(f.CompanyId, sales, f.Owner, Fixture.Key(), new(account.Version, false, "تعطيل"), default);
            await Error("invalid_group_account", () => Status(f, group.ResourceId, true));
        });
        test("INV02: edit replay stale version and rollback preserve card and audit", async f =>
        {
            var r = await Create(f, await Sales(f), "A"); var request = Edit(await Get(f, r.ResourceId)) with { Name = "معدل" }; var key = Fixture.Key();
            var audit = f.Store.Count<AuditEvent>(); f.Store.FailBeforeCommit = true;
            try { await f.Service.EditItemGroup(f.CompanyId, r.ResourceId, f.Owner, key, request, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await Get(f, r.ResourceId)).Version == r.Version && f.Store.Count<AuditEvent>() == audit);
            var edited = await f.Service.EditItemGroup(f.CompanyId, r.ResourceId, f.Owner, key, request, default);
            Ensure(await f.Service.EditItemGroup(f.CompanyId, r.ResourceId, f.Owner, key, request, default) == edited);
            await Error("version_conflict", () => f.Service.EditItemGroup(f.CompanyId, r.ResourceId, f.Owner, Fixture.Key(), request, default));
        });
        test("INV02: company references and permission boundaries enforced", async f =>
        {
            var sales = await Sales(f); await Error("permission_denied", () => f.Service.CreateItemGroup(f.CompanyId, f.Limited, Fixture.Key(), new("A", "مجموعة", sales), default));
            var cid = Guid.NewGuid(); await f.Service.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            var foreign = await f.Service.CreateAccount(cid, f.Owner, Fixture.Key(), new("SALES", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default);
            await Error("reference_not_found", () => Create(f, foreign.ResourceId, "BAD"));
            var foreignGroup = await f.Service.CreateItemGroup(cid, f.Owner, Fixture.Key(), new("FG", "مجموعة", foreign.ResourceId), default);
            await Error("reference_not_found", () => Create(f, sales, "BAD", foreignGroup.ResourceId));
            await Error("all_branches_required", () => f.Service.ListAccountItemGroups(f.CompanyId, sales, f.Limited, 0, 10, default));
        });
    }
    private static async Task<Guid> Sales(Fixture f) => (await f.Service.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("SALES", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default)).ResourceId;
    private static Task<CommandReceipt> Create(Fixture f, Guid sales, string code, Guid? parent = null) => f.Service.CreateItemGroup(f.CompanyId, f.Owner, Fixture.Key(), new(code, "مجموعة", sales, parent, EnglishName: "Group"), default);
    private static Task<ItemGroup> Get(Fixture f, Guid id) => f.Service.GetItemGroup(f.CompanyId, id, f.Owner, default);
    private static Task<CommandReceipt> Status(Fixture f, Guid id, bool active) => f.Service.SetItemGroupStatus(f.CompanyId, id, f.Owner, Fixture.Key(), new(f.Store.All<ItemGroup>().Single(x => x.Id == id).Version, active, "حالة"), default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Item group assertion failed"); }
    private static async Task Error(string code, Func<Task> action) { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
