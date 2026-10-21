using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private static object GroupSnapshot(ItemGroup x) => new
    { x.Version, x.Revision, x.Code, x.Name, x.EnglishName, x.Notes, x.ParentId, x.SalesAccountId, x.InventoryAccountId, x.CostAccountId, x.IsActive };
    private async Task ValidateGroupTree(Guid cid, Guid id, Guid? parentId, CancellationToken ct)
    {
        var seen = new HashSet<Guid> { id };
        while (parentId is Guid parent)
        {
            Check.That(seen.Add(parent), "item_group_cycle", "لا يسمح بدورة في شجرة المجموعات.");
            var group = await Required<ItemGroup>(cid, parent, ct);
            Check.That(group.IsActive, "inactive_parent_group", "المجموعة الأب معطلة."); parentId = group.ParentId;
        }
    }
    private async Task ValidateGroupAccounts(Guid cid, ItemGroup group, CancellationToken ct)
    {
        async Task Validate(Guid id, AccountType type, bool control)
        {
            var account = await Required<Account>(cid, id, ct);
            Check.That(account.IsActive && account.IsPosting && account.Type == type && (!control || account.IsControl),
                "invalid_group_account", "حساب المجموعة يجب أن يكون فعالًا وتفصيليًا ومن التصنيف المناسب؛ حساب المخزون رقابي.");
        }
        await Validate(group.SalesAccountId, AccountType.Revenue, false);
        if (group.InventoryAccountId is Guid inventory) await Validate(inventory, AccountType.Asset, true);
        if (group.CostAccountId is Guid cost) await Validate(cost, AccountType.Expense, false);
    }
    private async Task UniqueGroupCode(Guid cid, Guid id, string code, CancellationToken ct) =>
        Check.That(!(await store.Find<ItemGroup>(cid, x => x.Id != id && x.Code == code, ct)).Any(), "duplicate_item_group", "رمز المجموعة مكرر.", ErrorKind.Conflict);
    public Task<CommandReceipt> CreateItemGroup(Guid cid, Actor actor, string key, CreateItemGroupRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "item-group.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var group = new ItemGroup(cid, request.Code, request.Name, request.SalesAccountId, request.ParentId, request.InventoryAccountId, request.CostAccountId, request.EnglishName, request.Notes);
            await UniqueGroupCode(cid, group.Id, group.Code, token); await ValidateGroupTree(cid, group.Id, group.ParentId, token);
            await ValidateGroupAccounts(cid, group, token);
            store.Add(group); Audit(cid, actor, "item-group.created", group, new { after = GroupSnapshot(group) }); return Receipt(group, "Created");
        }, ct);
    public Task<CommandReceipt> EditItemGroup(Guid cid, Guid id, Actor actor, string key, EditItemGroupRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "item-group.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var group = await Required<ItemGroup>(cid, id, token); Check.Version(group, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            var candidate = new ItemGroup(cid, request.Code, request.Name, request.SalesAccountId, request.ParentId, request.InventoryAccountId, request.CostAccountId, request.EnglishName, request.Notes);
            await UniqueGroupCode(cid, id, candidate.Code, token);
            if (candidate.ParentId != group.ParentId) await ValidateGroupTree(cid, id, candidate.ParentId, token);
            if (group.Code != candidate.Code || group.ParentId != candidate.ParentId || group.SalesAccountId != candidate.SalesAccountId || group.InventoryAccountId != candidate.InventoryAccountId || group.CostAccountId != candidate.CostAccountId)
            {
                var groups = await store.Find<ItemGroup>(cid, x => true, token); var ids = new HashSet<Guid> { id }; var pending = new Stack<Guid>(); pending.Push(id);
                while (pending.TryPop(out var parent)) foreach (var child in groups.Where(x => x.ParentId == parent))
                { Check.That(ids.Add(child.Id), "item_group_cycle", "دورة في المجموعات."); pending.Push(child.Id); }
                var usedIds = ids.ToArray();
                Check.That(!(await store.Find<Item>(cid, x => usedIds.Contains(x.GroupId), token)).Any(), "item_group_in_use", "المجموعة أو أحد فروعها مرتبط بصنف؛ لا يمكن تغيير البنية.", ErrorKind.Conflict);
            }
            if (group.IsActive || candidate.SalesAccountId != group.SalesAccountId || candidate.InventoryAccountId != group.InventoryAccountId || candidate.CostAccountId != group.CostAccountId)
                await ValidateGroupAccounts(cid, candidate, token);
            var before = GroupSnapshot(group);
            group.Configure(candidate.Code, candidate.Name, candidate.SalesAccountId, candidate.ParentId, candidate.InventoryAccountId, candidate.CostAccountId, candidate.EnglishName, candidate.Notes);
            Audit(cid, actor, "item-group.edited", group, new { before, after = GroupSnapshot(group), reason }); return Receipt(group, "Updated");
        }, ct);
    public Task<CommandReceipt> SetItemGroupStatus(Guid cid, Guid id, Actor actor, string key, SetItemGroupStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "item-group.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var group = await Required<ItemGroup>(cid, id, token); Check.Version(group, request.ExpectedVersion); var reason = Check.Text(request.Reason, "السبب", 500);
            if (request.IsActive) { await ValidateGroupTree(cid, id, group.ParentId, token); await ValidateGroupAccounts(cid, group, token); }
            else
            {
                Check.That(!(await store.Find<Item>(cid, x => x.GroupId == id && x.IsActive, token)).Any(), "group_used_by_active_item", "المجموعة مرتبطة بصنف فعال.", ErrorKind.Conflict);
                Check.That(!(await store.Find<ItemGroup>(cid, x => x.ParentId == id && x.IsActive, token)).Any(), "active_child_groups", "عطل المجموعات التابعة أولًا.", ErrorKind.Conflict);
            }
            var before = GroupSnapshot(group); group.SetActive(request.IsActive);
            Audit(cid, actor, "item-group.status-changed", group, new { before, after = GroupSnapshot(group), reason }); return Receipt(group, group.IsActive ? "Active" : "Inactive");
        }, ct);
    public async Task<ItemGroup> GetItemGroup(Guid cid, Guid id, Actor actor, CancellationToken ct)
    { await Authorize(cid, actor, Permissions.Read, null, ct); return await Required<ItemGroup>(cid, id, ct); }
    public async Task<Page<ItemGroup>> SearchItemGroups(Guid cid, Actor actor, string? search, Guid? parentId, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        if (parentId is Guid parent) await Required<ItemGroup>(cid, parent, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<ItemGroup>(cid, x => (parentId == null || x.ParentId == parentId) && (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AuditEvent>> ListItemGroupAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await GetItemGroup(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && (x.Action == "item-group.created" || x.Action == "item-group.edited" || x.Action == "item-group.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<ItemGroup>> ListAccountItemGroups(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Required<Account>(cid, id, ct);
        return await store.ReadPage<ItemGroup>(cid, x => x.SalesAccountId == id || x.InventoryAccountId == id || x.CostAccountId == id,
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
}
