using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class InventoryIdentityService
{
    private async Task<(InventoryIdentity Identity, Guid[] Branches)> UsageScope(
        Guid cid, Guid itemId, Guid id, Actor actor, ItemTracking tracking, CancellationToken ct)
    {
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        await Required<Item>(cid, itemId, ct);
        var identity = await Required<InventoryIdentity>(cid, id, ct);
        Check.That(identity.ItemId == itemId && identity.Tracking == tracking, "reference_not_found",
            "هوية المخزون لا تتبع الصنف المحدد.", ErrorKind.NotFound);
        return (identity, await AllowedBranches(cid, member, null, ct));
    }

    private async Task<InventoryResourceUsage> Usage(Guid cid, Guid itemId, Guid id, Actor actor,
        ItemTracking tracking, CancellationToken ct)
    {
        var (_, branches) = await UsageScope(cid, itemId, id, actor, tracking, ct);
        var movements = await store.Find<StockMovement>(cid,
            x => x.IdentityId == id && branches.Contains(x.BranchId), ct);
        return InventoryResourceUsage.FromMovements(id, movements);
    }

    private async Task<Page<StockMovement>> UsageMovements(Guid cid, Guid itemId, Guid id, Actor actor,
        ItemTracking tracking, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        var (_, branches) = await UsageScope(cid, itemId, id, actor, tracking, ct);
        return await store.ReadPage<StockMovement>(cid,
            x => x.IdentityId == id && branches.Contains(x.BranchId),
            q => q.OrderBy(x => x.AccountingDate).ThenBy(x => x.Id), offset, limit, ct);
    }

    public Task<InventoryResourceUsage> GetLotUsage(Guid cid, Guid itemId, Guid id, Actor actor, CancellationToken ct) =>
        Usage(cid, itemId, id, actor, ItemTracking.Lot, ct);

    public Task<InventoryResourceUsage> GetSerialUsage(Guid cid, Guid itemId, Guid id, Actor actor, CancellationToken ct) =>
        Usage(cid, itemId, id, actor, ItemTracking.Serial, ct);

    public Task<Page<StockMovement>> ListLotMovements(Guid cid, Guid itemId, Guid id, Actor actor,
        int offset, int limit, CancellationToken ct) =>
        UsageMovements(cid, itemId, id, actor, ItemTracking.Lot, offset, limit, ct);

    public Task<Page<StockMovement>> ListSerialMovements(Guid cid, Guid itemId, Guid id, Actor actor,
        int offset, int limit, CancellationToken ct) =>
        UsageMovements(cid, itemId, id, actor, ItemTracking.Serial, offset, limit, ct);
}
