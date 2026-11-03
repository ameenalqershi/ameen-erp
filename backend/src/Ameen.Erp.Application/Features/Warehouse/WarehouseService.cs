using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class WarehouseService : ApplicationService
{
    public WarehouseService(IErpStore store, ITransactionRunner transactions, IClock clock) : base(store, transactions, clock)
    {
    }


    private static object WarehouseSnapshot(Warehouse w) => new { w.Version, w.Revision, w.Code, w.Name, w.EnglishName, w.Notes,
        w.BranchId, w.KeeperMembershipId, w.InventoryAccountId, w.Type, w.TrackLocations, w.IsActive, w.Locations };
    private async Task ValidateWarehouseReferences(Warehouse w, CancellationToken ct)
    {
        var branch = await Required<Branch>(w.CompanyId, w.BranchId, ct);
        Check.That(branch.IsActive && branch.IsProfileComplete, "invalid_warehouse_branch", "فرع المخزن يجب أن يكون فعالًا ومكتمل البيانات.");
        var keeper = await Required<Membership>(w.CompanyId, w.KeeperMembershipId, ct);
        Check.That(keeper.IsActive && keeper.Allows(w.BranchId), "invalid_warehouse_keeper", "أمين المخزن يجب أن يكون فعالًا ومصرحًا بالفرع.");
        var account = await Required<Account>(w.CompanyId, w.InventoryAccountId, ct);
        Check.That(account.IsActive && account.IsPosting && account.IsControl && account.Type == AccountType.Asset,
            "invalid_warehouse_account", "حساب المخزون يجب أن يكون أصلًا فعالًا وتفصيليًا ورقابيًا.");
    }
    private static List<WarehouseLocation> WarehouseLocations(Warehouse w, WarehouseLocationRequest[]? lines)
    {
        Check.That(lines is not null && lines.Length <= 500, "invalid_warehouse_locations", "سطور مواقع المخزن مطلوبة بحد أقصى 500.");
        return lines!.Select(x => { Check.That(x is not null, "invalid_warehouse_location", "سطر الموقع مطلوب.");
            return new WarehouseLocation(w.CompanyId, w.Id, x!.Code, x.Description); }).ToList();
    }
    private async Task UniqueWarehouseCode(Warehouse w, Guid id, CancellationToken ct) =>
        Check.That(!(await store.Find<Warehouse>(w.CompanyId, x => x.Id != id && x.Code == w.Code, ct)).Any(),
            "duplicate_warehouse", "رمز المخزن مكرر في الشركة.", ErrorKind.Conflict);
    public Task<CommandReceipt> CreateWarehouse(Guid cid, Actor actor, string key, CreateWarehouseRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "warehouse.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var w = new Warehouse(cid, request.Code, request.Name, request.BranchId, request.KeeperMembershipId, request.InventoryAccountId, request.Type, request.TrackLocations, request.EnglishName, request.Notes);
            await ValidateWarehouseReferences(w, token); await UniqueWarehouseCode(w, w.Id, token);
            w.SetLocations(WarehouseLocations(w, request.Locations)); store.Add(w);
            Audit(cid, actor, "warehouse.created", w, new { after = WarehouseSnapshot(w) }); return Receipt(w, "Created");
        }, ct);
    public Task<CommandReceipt> EditWarehouse(Guid cid, Guid id, Actor actor, string key, EditWarehouseRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "warehouse.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var w = await Required<Warehouse>(cid, id, token); Check.Version(w, request.ExpectedVersion); var reason = Check.Text(request.Reason, "السبب", 500);
            var candidate = new Warehouse(cid, request.Code, request.Name, request.BranchId, request.KeeperMembershipId, request.InventoryAccountId, request.Type, request.TrackLocations, request.EnglishName, request.Notes);
            if ((await store.Find<StockMovement>(cid, x => x.WarehouseId == id, token)).Any())
                Check.That(w.Code == candidate.Code && w.BranchId == candidate.BranchId && w.InventoryAccountId == candidate.InventoryAccountId &&
                    w.Type == candidate.Type && w.TrackLocations == candidate.TrackLocations, "warehouse_stock_structure_in_use", "بنية المخزن مقفلة بعد حركة المخزون.", ErrorKind.Conflict);
            await ValidateWarehouseReferences(candidate, token); await UniqueWarehouseCode(candidate, id, token);
            var before = WarehouseSnapshot(w);
            w.Configure(candidate.Code, candidate.Name, candidate.BranchId, candidate.KeeperMembershipId, candidate.InventoryAccountId, candidate.Type, candidate.TrackLocations, candidate.EnglishName, candidate.Notes);
            var locations = WarehouseLocations(w, request.Locations);
            store.RemoveWarehouseLocations(w.Locations.ToArray()); w.SetLocations(locations);
            Audit(cid, actor, "warehouse.edited", w, new { before, after = WarehouseSnapshot(w), reason }); return Receipt(w, "Updated");
        }, ct);
    public Task<CommandReceipt> SetWarehouseStatus(Guid cid, Guid id, Actor actor, string key, SetWarehouseStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "warehouse.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var w = await Required<Warehouse>(cid, id, token); Check.Version(w, request.ExpectedVersion); var reason = Check.Text(request.Reason, "السبب", 500);
            if (request.IsActive) await ValidateWarehouseReferences(w, token);
            var before = WarehouseSnapshot(w); w.SetActive(request.IsActive);
            Audit(cid, actor, "warehouse.status-changed", w, new { before, after = WarehouseSnapshot(w), reason }); return Receipt(w, w.IsActive ? "Active" : "Inactive");
        }, ct);
    public async Task<Warehouse> GetWarehouse(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct); var w = await Required<Warehouse>(cid, id, ct);
        await Authorize(cid, actor, Permissions.Read, w.BranchId, ct); return w;
    }
    public async Task<Page<Warehouse>> SearchWarehouses(Guid cid, Actor actor, string? search, Guid? branchId, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        var branches = await AllowedBranches(cid, member, branchId, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<Warehouse>(cid, x => branches.Contains(x.BranchId) && (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AuditEvent>> ListWarehouseAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await GetWarehouse(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && (x.Action == "warehouse.created" || x.Action == "warehouse.edited" || x.Action == "warehouse.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<Warehouse>> ListAccountWarehouses(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Required<Account>(cid, id, ct);
        return await store.ReadPage<Warehouse>(cid, x => x.InventoryAccountId == id, q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
}
