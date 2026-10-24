using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class InventoryOpeningService
{
    public async Task<InventoryOpening> Get(Guid cid, Guid id, Actor actor, CancellationToken ct)
    { await AuthorizeOpening(cid, id, actor, Permissions.Read, ct); return await Required<InventoryOpening>(cid, id, ct); }
    public async Task<Page<InventoryOpening>> Search(Guid cid, Actor actor, Guid? branchId, InventoryOpeningState? state,
        string? search, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        Check.That(state is null || Enum.IsDefined(state.Value), "invalid_opening_state", "حالة غير معروفة.");
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث طويلة."); search = search?.Trim();
        var allowed = await AllowedBranches(cid, member, branchId, ct);
        return await store.ReadPage<InventoryOpening>(cid, x => allowed.Contains(x.BranchId) && (state == null || x.State == state) &&
            (search == null || x.Number.Contains(search) || x.CountReference.Contains(search) || x.Description.Contains(search)),
            q => q.OrderByDescending(x => x.AccountingDate).ThenBy(x => x.Number), offset, limit, ct);
    }
    public async Task<InventoryOpeningPreview> Preview(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var opening = await Get(cid, id, actor, ct);
        var value = opening.Lines.Sum(x => x.Value);
        if (opening.State == InventoryOpeningState.Posted)
            return new(id, opening.Version, opening.CurrencyCode, opening.ReversesOpeningId is null ? value : -value, opening.Lines.Count, false, "already_posted", clock.UtcNow);
        await ValidateSnapshot(opening, ct);
        Check.That(opening.State != InventoryOpeningState.Cancelled, "opening_state_conflict", "المستند ملغى.", ErrorKind.Conflict);
        if (opening.ReversesOpeningId is not null)
        {
            var reversalMember = await Authorize(cid, actor, Permissions.Read, opening.BranchId, ct);
            var ready = opening.State == InventoryOpeningState.Approved && reversalMember.Has(Permissions.InventoryReverse) && reversalMember.Has(Permissions.InventoryPost);
            return new(id, opening.Version, opening.CurrencyCode, -value, opening.Lines.Count, ready,
                ready ? "" : "reversal_approval_or_permission_required", clock.UtcNow);
        }
        InventoryOpeningPostingPlan plan;
        try { plan = await PlanPosting(opening, ct); }
        catch (BusinessException e) { return new(id, opening.Version, opening.CurrencyCode, value, opening.Lines.Count, false, e.Code, clock.UtcNow); }
        var member = await Authorize(cid, actor, Permissions.Read, opening.BranchId, ct);
        var blocker = opening.State != InventoryOpeningState.Approved ? "opening_not_approved" :
            !member.Has(Permissions.InventoryPost) ? "posting_permission_required" : "";
        return new(id, opening.Version, opening.CurrencyCode, value, opening.Lines.Count, blocker.Length == 0, blocker, clock.UtcNow, plan);
    }
    public async Task<Page<AuditEvent>> AuditEvents(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await Get(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && x.Action.StartsWith("inventory-opening."),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
