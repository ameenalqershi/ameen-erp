using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private static object CenterSnapshot(CostCenter x) => new
    { x.Version, x.Revision, x.Code, x.Name, x.EnglishName, x.Notes, x.ParentId, x.BranchId, x.ResponsibleMembershipId, x.IsActive };

    private async Task ValidateCenterResponsible(Guid cid, Guid? membershipId, Guid? branchId, CancellationToken ct)
    {
        if (membershipId is not Guid id) return;
        var member = await Required<Membership>(cid, id, ct);
        Check.That(member.IsActive && (branchId is Guid branch ? member.Allows(branch) : member.AllBranches),
            "invalid_center_responsible", "مسؤول المركز يجب أن يكون فعالًا ومصرحًا بنطاق المركز.");
    }
    private async Task ValidateCenterTree(Guid cid, Guid id, Guid? parentId, Guid? branchId, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { id };
        while (parentId is Guid parent)
        {
            Check.That(visited.Add(parent), "center_tree_cycle", "لا يمكن ربط المركز بنفسه أو بأحد فروعه.");
            var ancestor = await Required<CostCenter>(cid, parent, ct);
            Check.That(ancestor.IsActive, "inactive_parent_center", "المركز الأب معطل.");
            Check.That(ancestor.BranchId is null || ancestor.BranchId == branchId,
                "center_branch_mismatch", "المركز الفرعي يجب أن يكون ضمن فرع المركز الأب أو تحت مركز عام.");
            parentId = ancestor.ParentId;
        }
    }
    private static Guid[] CenterSubtree(Guid id, IReadOnlyList<CostCenter> centers)
    {
        var children = centers.Where(x => x.ParentId is not null).ToLookup(x => x.ParentId!.Value);
        var visited = new HashSet<Guid>(); var pending = new Stack<Guid>(); pending.Push(id);
        while (pending.TryPop(out var current))
        {
            Check.That(visited.Add(current), "invalid_center_tree", "توجد دورة في شجرة المراكز.", ErrorKind.Conflict);
            foreach (var child in children[current]) pending.Push(child.Id);
        }
        return visited.ToArray();
    }
    private async Task<bool> HasCenterUsage(Guid cid, Guid[] ids, CancellationToken ct) =>
        (await store.Find<JournalEntry>(cid, x => x.Lines.Any(l => l.CostCenterId != null && ids.Contains(l.CostCenterId.Value)), ct)).Count > 0 ||
        (await store.Find<RecurringJournalTemplate>(cid, x => x.Lines.Any(l => l.CostCenterId != null && ids.Contains(l.CostCenterId.Value)), ct)).Count > 0;

    public Task<CommandReceipt> EditCostCenter(Guid cid, Guid id, Actor actor, string key, EditCostCenterRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "cost-center.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var center = await Required<CostCenter>(cid, id, token); Check.Version(center, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            var candidate = new CostCenter(cid, request.Code, request.Name, request.BranchId, request.ParentId,
                request.ResponsibleMembershipId, request.EnglishName, request.Notes);
            var structureChanged = center.Code != candidate.Code || center.ParentId != candidate.ParentId || center.BranchId != candidate.BranchId;
            if (structureChanged)
            {
                if (candidate.BranchId is Guid branch) await ActiveBranch(cid, branch, token);
                await ValidateCenterTree(cid, id, candidate.ParentId, candidate.BranchId, token);
                var centers = await store.Find<CostCenter>(cid, x => true, token);
                Check.That(!centers.Any(x => x.Id != id && x.Code == candidate.Code), "duplicate_code", "رمز المركز مكرر.", ErrorKind.Conflict);
                var subtree = CenterSubtree(id, centers);
                Check.That(candidate.BranchId is null || centers.Where(x => x.Id != id && subtree.Contains(x.Id)).All(x => x.BranchId == candidate.BranchId),
                    "center_branch_mismatch", "نطاق الفرع الجديد لا يشمل المراكز التابعة.");
                Check.That(!await HasCenterUsage(cid, subtree, token), "center_structure_in_use", "المركز أو أحد فروعه مستخدم؛ لا يمكن تغيير بنيته.", ErrorKind.Conflict);
            }
            if (center.ResponsibleMembershipId != candidate.ResponsibleMembershipId || center.BranchId != candidate.BranchId)
                await ValidateCenterResponsible(cid, candidate.ResponsibleMembershipId, candidate.BranchId, token);
            var before = CenterSnapshot(center);
            center.Configure(candidate.Code, candidate.Name, candidate.BranchId, candidate.ParentId, candidate.ResponsibleMembershipId, candidate.EnglishName, candidate.Notes);
            Audit(cid, actor, "cost-center.edited", center, new { before, after = CenterSnapshot(center), reason });
            return Receipt(center, "Updated");
        }, ct);

    public Task<CommandReceipt> SetCostCenterStatus(Guid cid, Guid id, Actor actor, string key, SetCostCenterStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "cost-center.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var center = await Required<CostCenter>(cid, id, token); Check.Version(center, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب تغيير الحالة", 500);
            if (!request.IsActive)
                Check.That(!(await store.Find<CostCenter>(cid, x => x.ParentId == id && x.IsActive, token)).Any(),
                    "active_child_centers", "عطل المراكز الفرعية أولًا.", ErrorKind.Conflict);
            else
            {
                if (center.BranchId is Guid branch) await ActiveBranch(cid, branch, token);
                await ValidateCenterTree(cid, id, center.ParentId, center.BranchId, token);
            }
            var before = CenterSnapshot(center); center.SetActive(request.IsActive);
            Audit(cid, actor, "cost-center.status-changed", center, new { before, after = CenterSnapshot(center), reason });
            return Receipt(center, center.IsActive ? "Active" : "Inactive");
        }, ct);

    public async Task<CostCenter> GetCostCenter(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        var center = await Required<CostCenter>(cid, id, ct);
        Check.That(center.BranchId is null || member.Allows(center.BranchId.Value), "branch_access_denied", "المركز خارج نطاق الفروع المسموحة.", ErrorKind.Forbidden);
        return center;
    }
    public async Task<Page<CostCenter>> SearchCostCenters(Guid cid, Actor actor, string? search, Guid? branchId, Guid? parentId,
        bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        var branches = await AllowedBranches(cid, member, branchId, ct);
        if (parentId is Guid parent) await GetCostCenter(cid, parent, actor, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<CostCenter>(cid, x => (x.BranchId == null || branches.Contains(x.BranchId.Value)) &&
            (parentId == null || x.ParentId == parentId) && (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<CostCenterUsage> GetCostCenterUsage(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Required<CostCenter>(cid, id, ct);
        var centers = await store.Find<CostCenter>(cid, x => true, ct);
        var journals = await store.Find<JournalEntry>(cid, x => x.Lines.Any(l => l.CostCenterId == id), ct);
        var templates = await store.Find<RecurringJournalTemplate>(cid, x => x.Lines.Any(l => l.CostCenterId == id), ct);
        var refs = journals.Select(x => new AccountUsageReference("Journal", x.Id, x.BranchId, x.Number, x.State.ToString()))
            .Concat(templates.Select(x => new AccountUsageReference("RecurringTemplate", x.Id, x.BranchId, x.Name, x.IsActive ? "Active" : "Inactive")))
            .OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.NumberOrName, StringComparer.Ordinal).ThenBy(x => x.ResourceId).ToArray();
        return new(id, centers.Count(x => x.ParentId == id), journals.Count, templates.Count, await HasCenterUsage(cid, CenterSubtree(id, centers), ct),
            new(refs.Skip(offset).Take(limit).ToArray(), refs.Length, offset, limit));
    }
    public async Task<Page<AuditEvent>> ListCostCenterAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await GetCostCenter(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
            (x.Action == "cost-center.created" || x.Action == "cost-center.edited" || x.Action == "cost-center.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
