using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class WarehouseService
{
    public async Task<InventoryResourceUsage> GetWarehouseUsage(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await GetWarehouse(cid, id, actor, ct);
        var movements = await store.Find<StockMovement>(cid, x => x.WarehouseId == id, ct);
        return InventoryResourceUsage.FromMovements(id, movements);
    }

    public async Task<Page<StockMovement>> ListWarehouseMovements(Guid cid, Guid id, Actor actor,
        int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        await GetWarehouse(cid, id, actor, ct);
        return await store.ReadPage<StockMovement>(cid, x => x.WarehouseId == id,
            q => q.OrderBy(x => x.AccountingDate).ThenBy(x => x.Id), offset, limit, ct);
    }
}
