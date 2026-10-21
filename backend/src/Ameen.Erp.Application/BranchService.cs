using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private static object BranchSnapshot(Branch x) => new
    { x.Version, x.Revision, x.Code, x.Name, x.EnglishName, x.Notes, x.City, x.Address, x.ParentId, x.DefaultCostCenterId, x.IsActive };

    private async Task ValidateBranchTree(Guid cid, Guid id, Guid? parentId, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { id };
        while (parentId is Guid parent)
        {
            Check.That(visited.Add(parent), "branch_tree_cycle", "لا يمكن ربط الفرع بنفسه أو بأحد فروعه.");
            var ancestor = await Required<Branch>(cid, parent, ct);
            Check.That(ancestor.IsActive, "inactive_parent_branch", "الفرع الأب معطل.");
            parentId = ancestor.ParentId;
        }
    }
    private async Task ValidateBranchDefaultCenter(Guid cid, Guid id, Guid? centerId, CancellationToken ct)
    {
        if (centerId is not Guid center) return;
        var value = await Required<CostCenter>(cid, center, ct);
        Check.That(value.IsActive && (value.BranchId is null || value.BranchId == id),
            "invalid_branch_default_center", "المركز الافتراضي يجب أن يكون فعالًا وعامًا أو تابعًا لهذا الفرع.");
    }
    private static Guid[] BranchSubtree(Guid id, IReadOnlyList<Branch> branches)
    {
        var children = branches.Where(x => x.ParentId is not null).ToLookup(x => x.ParentId!.Value);
        var visited = new HashSet<Guid>(); var pending = new Stack<Guid>(); pending.Push(id);
        while (pending.TryPop(out var current))
        {
            Check.That(visited.Add(current), "invalid_branch_tree", "توجد دورة في شجرة الفروع.", ErrorKind.Conflict);
            foreach (var child in children[current]) pending.Push(child.Id);
        }
        return visited.ToArray();
    }
    private async Task<bool> HasBranchUsage(Guid cid, Guid[] ids, CancellationToken ct) =>
        (await store.Find<JournalEntry>(cid, x => ids.Contains(x.BranchId), ct)).Count > 0 ||
        (await store.Find<RecurringJournalTemplate>(cid, x => ids.Contains(x.BranchId), ct)).Count > 0;

    public Task<CommandReceipt> EditBranch(Guid cid, Guid id, Actor actor, string key, EditBranchRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "branch.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var branch = await Required<Branch>(cid, id, token); Check.Version(branch, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            var candidate = new Branch(cid, request.Code, request.Name, request.City, request.Address, request.ParentId,
                request.DefaultCostCenterId, request.EnglishName, request.Notes);
            if (candidate.Code != branch.Code || candidate.ParentId != branch.ParentId)
            {
                await ValidateBranchTree(cid, id, candidate.ParentId, token);
                var branches = await store.Find<Branch>(cid, x => true, token);
                Check.That(!branches.Any(x => x.Id != id && x.Code == candidate.Code), "duplicate_code", "رمز الفرع مكرر.", ErrorKind.Conflict);
                Check.That(!await HasBranchUsage(cid, BranchSubtree(id, branches), token), "branch_structure_in_use",
                    "الفرع أو أحد فروعه مستخدم؛ لا يمكن تغيير رمزه أو بنيته.", ErrorKind.Conflict);
            }
            await ValidateBranchDefaultCenter(cid, id, candidate.DefaultCostCenterId, token);
            var before = BranchSnapshot(branch);
            branch.Configure(candidate.Code, candidate.Name, candidate.City!, candidate.Address!, candidate.ParentId,
                candidate.DefaultCostCenterId, candidate.EnglishName, candidate.Notes);
            Audit(cid, actor, "branch.edited", branch, new { before, after = BranchSnapshot(branch), reason });
            return Receipt(branch, "Updated");
        }, ct);

    public Task<CommandReceipt> SetBranchStatus(Guid cid, Guid id, Actor actor, string key, SetBranchStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "branch.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var branch = await Required<Branch>(cid, id, token); Check.Version(branch, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب تغيير الحالة", 500);
            if (!request.IsActive)
                Check.That(!(await store.Find<Branch>(cid, x => x.ParentId == id && x.IsActive, token)).Any(),
                    "active_child_branches", "عطل الفروع التابعة أولًا.", ErrorKind.Conflict);
            else
            {
                Check.That(branch.IsProfileComplete, "branch_profile_incomplete", "استكمل المدينة والعنوان قبل التفعيل.");
                await ValidateBranchTree(cid, id, branch.ParentId, token);
                await ValidateBranchDefaultCenter(cid, id, branch.DefaultCostCenterId, token);
            }
            var before = BranchSnapshot(branch); branch.SetActive(request.IsActive);
            Audit(cid, actor, "branch.status-changed", branch, new { before, after = BranchSnapshot(branch), reason });
            return Receipt(branch, branch.IsActive ? "Active" : "Inactive");
        }, ct);

    public async Task<Branch> GetBranch(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, id, ct); return await Required<Branch>(cid, id, ct);
    }
    public async Task<Page<Branch>> SearchBranches(Guid cid, Actor actor, string? search, Guid? parentId,
        bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        var branches = await AllowedBranches(cid, member, null, ct);
        if (parentId is Guid parent) await GetBranch(cid, parent, actor, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<Branch>(cid, x => branches.Contains(x.Id) &&
            (parentId == null || x.ParentId == parentId) && (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<BranchUsage> GetBranchUsage(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Required<Branch>(cid, id, ct);
        var branches = await store.Find<Branch>(cid, x => true, ct);
        var memberships = await store.Find<Membership>(cid, x => true, ct);
        return new(id, branches.Count(x => x.ParentId == id), (await store.Find<CostCenter>(cid, x => x.BranchId == id, ct)).Count,
            memberships.Count(x => x.AllBranches || x.BranchIds.Contains(id)),
            (await store.Find<JournalEntry>(cid, x => x.BranchId == id, ct)).Count,
            (await store.Find<RecurringJournalTemplate>(cid, x => x.BranchId == id, ct)).Count,
            await HasBranchUsage(cid, BranchSubtree(id, branches), ct));
    }
    public async Task<Page<AuditEvent>> ListBranchAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await GetBranch(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
            (x.Action == "branch.created" || x.Action == "branch.edited" || x.Action == "branch.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
