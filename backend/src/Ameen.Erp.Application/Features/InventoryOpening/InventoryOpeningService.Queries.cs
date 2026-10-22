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
        var opening = await Get(cid, id, actor, ct); await ValidateSnapshot(opening, ct);
        Check.That(opening.State != InventoryOpeningState.Cancelled, "opening_state_conflict", "المستند ملغى.", ErrorKind.Conflict);
        return new(id, opening.Version, opening.CurrencyCode, opening.Lines.Sum(x => x.Value), opening.Lines.Count,
            false, "inventory_ledger_and_opening_reconciliation_pending", clock.UtcNow);
    }
    public async Task<Page<AuditEvent>> AuditEvents(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await Get(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && x.Action.StartsWith("inventory-opening."),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
