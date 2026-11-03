using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ItemService
{
    private async Task<Guid[]> UsageBranches(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        await Required<Item>(cid, id, ct);
        return await AllowedBranches(cid, member, null, ct);
    }

    public async Task<InventoryResourceUsage> GetItemUsage(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var branches = await UsageBranches(cid, id, actor, ct);
        var movements = await store.Find<StockMovement>(cid, x => x.ItemId == id && branches.Contains(x.BranchId), ct);
        return InventoryResourceUsage.FromMovements(id, movements);
    }

    public async Task<Page<StockMovement>> ListItemMovements(Guid cid, Guid id, Actor actor,
        int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        var branches = await UsageBranches(cid, id, actor, ct);
        return await store.ReadPage<StockMovement>(cid, x => x.ItemId == id && branches.Contains(x.BranchId),
            q => q.OrderBy(x => x.AccountingDate).ThenBy(x => x.Id), offset, limit, ct);
    }
}
