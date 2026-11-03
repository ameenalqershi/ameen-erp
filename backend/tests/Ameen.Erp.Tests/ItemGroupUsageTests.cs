using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class ItemGroupUsageTests
{
    private sealed record Tree(Guid Root, Guid Child, Guid Leaf, Guid Other, CreateItemRequest Request);
    private static async Task<Tree> Setup(Fixture f)
    {
        var request = await ItemTests.Setup(f);
        var root = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, request.GroupId, f.Owner, default);
        async Task<Guid> Group(string code, Guid? parent) => (await f.Service.ItemGroup.CreateItemGroup(f.CompanyId, f.Owner, Fixture.Key(),
            new(code, code, root.SalesAccountId, parent, root.InventoryAccountId, root.CostAccountId), default)).ResourceId;
        var child = await Group("CHILD", root.Id); var leaf = await Group("LEAF", child); var other = await Group("OTHER", null);
        return new(root.Id, child, leaf, other, request);
    }
    private static Task<CommandReceipt> Add(Fixture f, Tree t, Guid group, string code) =>
        f.Service.Item.CreateItem(f.CompanyId, f.Owner, Fixture.Key(), t.Request with
        { GroupId = group, Code = code, Units = t.Request.Units.Select(x => x with { Barcode = null }).ToArray() }, default);
    private static Task<ItemGroupUsage> Usage(Fixture f, Guid group) => f.Service.ItemGroup.GetItemGroupUsage(f.CompanyId, group, f.Owner, default);
    private static Task<Page<Item>> Items(Fixture f, Guid group, bool descendants = true, bool? active = null, int offset = 0, int limit = 10) =>
        f.Service.ItemGroup.ListItemGroupItems(f.CompanyId, group, f.Owner, descendants, active, offset, limit, default);
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV02-AC01: item classification persists mapped accounts without unauthorized parent inheritance", async f =>
        {
            var request = await ItemTests.Setup(f);
            var root = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, request.GroupId, f.Owner, default);
            Ensure(root.SalesAccountId != Guid.Empty && root.InventoryAccountId is not null &&
                root.CostAccountId is not null && root.IsActive);
            var item = await f.Service.Item.CreateItem(f.CompanyId, f.Owner, Fixture.Key(),
                request with { Code = "CLASS-01" }, default);
            var saved = await f.Service.Item.GetItem(f.CompanyId, item.ResourceId, f.Owner, default);
            var mapped = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, saved.GroupId, f.Owner, default);
            Ensure(saved.GroupId == root.Id && saved.Type == ItemType.Goods &&
                mapped.SalesAccountId == root.SalesAccountId &&
                mapped.InventoryAccountId == root.InventoryAccountId &&
                mapped.CostAccountId == root.CostAccountId &&
                (await f.Service.ItemGroup.ListItemGroupItems(f.CompanyId, root.Id, f.Owner,
                    false, true, 0, 10, default)).Items.Single().Id == item.ResourceId);

            var childWithoutStock = await f.Service.ItemGroup.CreateItemGroup(f.CompanyId,
                f.Owner, Fixture.Key(), new("CLASS-SALES", "خدمات", root.SalesAccountId,
                    root.Id), default);
            var child = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, childWithoutStock.ResourceId,
                f.Owner, default);
            Ensure(child.ParentId == root.Id && child.InventoryAccountId is null &&
                child.CostAccountId is null && child.SalesAccountId == root.SalesAccountId);
            var service = await f.Service.Item.CreateItem(f.CompanyId, f.Owner, Fixture.Key(),
                request with { Code = "CLASS-SERVICE", Type = ItemType.Service,
                    Tracking = ItemTracking.None, GroupId = child.Id,
                    ShelfLifeDays = null, ReorderLevel = null,
                    Units = request.Units.Select(x => x with { Barcode = null }).ToArray() }, default);
            Ensure((await f.Service.Item.GetItem(f.CompanyId, service.ResourceId,
                f.Owner, default)).GroupId == child.Id);

            var audit = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            await Error("stock_accounts_required", () => f.Service.Item.CreateItem(f.CompanyId,
                f.Owner, Fixture.Key(), request with { Code = "NO-IMPLICIT-INHERIT",
                    GroupId = child.Id, Units = request.Units.Select(x => x with { Barcode = null }).ToArray() }, default));
            Ensure(f.Store.Count<AuditEvent>() == audit && f.Store.Count<IdempotencyRecord>() == receipts &&
                f.Store.Count<Item>() == 2 && f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0);
        });
        test("INV02-GAP02: stocked items require both accounts and lock mapped group structure", async f =>
        {
            var request = await ItemTests.Setup(f);
            var root = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, request.GroupId, f.Owner, default);
            var noStock = await f.Service.ItemGroup.CreateItemGroup(f.CompanyId, f.Owner, Fixture.Key(),
                new("NO-STOCK", "بلا حساب مخزون", root.SalesAccountId), default);
            await Error("stock_accounts_required", () => f.Service.Item.CreateItem(f.CompanyId, f.Owner,
                Fixture.Key(), request with { Code = "REJECT-STOCK", GroupId = noStock.ResourceId,
                    Units = request.Units.Select(x => x with { Barcode = null }).ToArray() }, default));
            var created = await Add(f, new Tree(root.Id, Guid.Empty, Guid.Empty, Guid.Empty, request), root.Id, "LOCK-STOCK");
            var saved = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, root.Id, f.Owner, default);
            await Error("item_group_in_use", () => f.Service.ItemGroup.EditItemGroup(f.CompanyId, root.Id,
                f.Owner, Fixture.Key(), ItemGroupTests.Edit(saved) with { Code = "OTHER" }, default));
            await f.Service.Item.SetItemStatus(f.CompanyId, created.ResourceId, f.Owner, Fixture.Key(),
                new(created.Version, false, "إيقاف"), default);
            var usage = await f.Service.ItemGroup.GetItemGroupUsage(f.CompanyId, root.Id, f.Owner, default);
            Ensure(usage.StructureLocked && usage.SubtreeItems == 1 && usage.ActiveSubtreeItems == 0 &&
                f.Store.Count<JournalEntry>() == 0 && f.Store.Count<StockMovement>() == 0);
        });
        test("INV02 usage: direct and descendant counts exclude unrelated groups without double counting", async f =>
        {
            var t = await Setup(f); await Add(f, t, t.Root, "01"); await Add(f, t, t.Child, "02"); await Add(f, t, t.Leaf, "03"); await Add(f, t, t.Other, "04");
            var u = await Usage(f, t.Root);
            Ensure(u.DirectChildGroups == 1 && u.ActiveDirectChildGroups == 1 && u.DescendantGroups == 2);
            Ensure(u.DirectItems == 1 && u.ActiveDirectItems == 1 && u.SubtreeItems == 3 && u.ActiveSubtreeItems == 3 && u.StructureLocked && u.HasDeactivationBlockers);
            Ensure(u.GroupVersion == (await f.Service.ItemGroup.GetItemGroup(f.CompanyId, t.Root, f.Owner, default)).Version);
            var child = await Usage(f, t.Child); Ensure(child.DirectItems == 1 && child.SubtreeItems == 2);
        });
        test("INV02 usage: disabled descendant item still locks ancestor structure and agrees with edit guard", async f =>
        {
            var t = await Setup(f); var item = await Add(f, t, t.Leaf, "01");
            await f.Service.Item.SetItemStatus(f.CompanyId, item.ResourceId, f.Owner, Fixture.Key(), new(item.Version, false, "إيقاف"), default);
            foreach (var id in new[] { t.Leaf, t.Child })
            {
                var g = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, id, f.Owner, default);
                await f.Service.ItemGroup.SetItemGroupStatus(f.CompanyId, id, f.Owner, Fixture.Key(), new(g.Version, false, "إيقاف"), default);
            }
            var u = await Usage(f, t.Root); Ensure(u.StructureLocked && !u.HasDeactivationBlockers && u.ActiveSubtreeItems == 0 && u.SubtreeItems == 1);
            var root = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, t.Root, f.Owner, default);
            await Error("item_group_in_use", () => f.Service.ItemGroup.EditItemGroup(f.CompanyId, t.Root, f.Owner, Fixture.Key(), ItemGroupTests.Edit(root) with { Code = "NEW" }, default));
            await f.Service.ItemGroup.SetItemGroupStatus(f.CompanyId, t.Root, f.Owner, Fixture.Key(), new(root.Version, false, "إيقاف"), default);
            Ensure((await Items(f, t.Root)).Items.Single().Id == item.ResourceId);
        });
        test("INV02 usage: stable pages and direct active filters include disabled references by default", async f =>
        {
            var t = await Setup(f); await Add(f, t, t.Leaf, "03"); var inactive = await Add(f, t, t.Root, "01"); await Add(f, t, t.Child, "02");
            await f.Service.Item.SetItemStatus(f.CompanyId, inactive.ResourceId, f.Owner, Fixture.Key(), new(inactive.Version, false, "إيقاف"), default);
            var page = await Items(f, t.Root, offset: 1, limit: 1); Ensure(page.TotalCount == 3 && page.Items.Single().Code == "02");
            Ensure((await Items(f, t.Root, descendants: false)).Items.Single().Id == inactive.ResourceId);
            Ensure((await Items(f, t.Root, active: true)).TotalCount == 2 && (await Items(f, t.Root, active: false)).TotalCount == 1);
            Ensure((await Items(f, t.Root, offset: 10)).Items.Count == 0);
            await Error("invalid_pagination", () => Items(f, t.Root, limit: 201));
            await Error("invalid_pagination", () => Items(f, t.Root, offset: -1));
        });
        test("INV02 usage: full-company read permission is required before disclosing group usage", async f =>
        {
            var t = await Setup(f);
            await Error("all_branches_required", () => f.Service.ItemGroup.GetItemGroupUsage(f.CompanyId, t.Root, f.Limited, default));
            await Error("all_branches_required", () => f.Service.ItemGroup.ListItemGroupItems(f.CompanyId, t.Root, f.Limited, true, null, 0, 10, default));
            await Error("permission_denied", () => f.Service.ItemGroup.GetItemGroupUsage(f.CompanyId, t.Root, new Actor("outsider", true), default));
            await Error("permission_denied", () => f.Service.ItemGroup.ListItemGroupItems(f.CompanyId, t.Root, new Actor("outsider"), true, null, 0, 10, default));
            Ensure((await f.Service.ItemGroup.GetItemGroupUsage(f.CompanyId, t.Root, f.Clerk, default)).SubtreeItems == 0);
        });
        test("INV02 usage: foreign-company group is rejected and reads leave no audit or financial effects", async f =>
        {
            var t = await Setup(f); var other = Guid.NewGuid();
            await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(other, "FOREIGN", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            await Error("reference_not_found", () => f.Service.ItemGroup.GetItemGroupUsage(other, t.Root, f.Owner, default));
            await Error("reference_not_found", () => f.Service.ItemGroup.ListItemGroupItems(other, t.Root, f.Owner, true, null, 0, 10, default));
            var audit = f.Store.Count<AuditEvent>(); var receipts = f.Store.Count<IdempotencyRecord>();
            var group = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, t.Root, f.Owner, default);
            await Usage(f, t.Root); await Items(f, t.Root);
            Ensure(f.Store.Count<AuditEvent>() == audit && f.Store.Count<IdempotencyRecord>() == receipts && f.Store.Count<JournalEntry>() == 0);
            Ensure(group.Version == (await f.Service.ItemGroup.GetItemGroup(f.CompanyId, t.Root, f.Owner, default)).Version);
        });
        test("INV02 usage: empty group has no locks; active child alone blocks deactivation", async f =>
        {
            var t = await Setup(f); var empty = await Usage(f, t.Other);
            Ensure(!empty.StructureLocked && !empty.HasDeactivationBlockers && empty.SubtreeItems == 0 && empty.DescendantGroups == 0);
            var root = await Usage(f, t.Root); Ensure(!root.StructureLocked && root.HasDeactivationBlockers);
            var g = await f.Service.ItemGroup.GetItemGroup(f.CompanyId, t.Root, f.Owner, default);
            await Error("active_child_groups", () => f.Service.ItemGroup.SetItemGroupStatus(f.CompanyId, t.Root, f.Owner, Fixture.Key(), new(g.Version, false, "إيقاف"), default));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Item group usage assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
