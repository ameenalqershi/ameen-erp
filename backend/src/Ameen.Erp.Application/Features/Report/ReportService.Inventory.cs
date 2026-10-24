using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class ReportService
{
    private async Task<Guid[]> InventoryBranches(Guid cid, Actor actor, Guid? branchId, Guid? warehouseId, CancellationToken ct)
    {
        var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        var branches = await AllowedBranches(cid, member, branchId, ct);
        if (warehouseId is Guid id)
        {
            var warehouse = await Required<Warehouse>(cid, id, ct);
            await Authorize(cid, actor, Permissions.Read, warehouse.BranchId, ct);
            Check.That(branchId is null || branchId == warehouse.BranchId, "warehouse_branch_mismatch", "المخزن لا يتبع الفرع المحدد.");
        }
        return branches;
    }
    public async Task<InventoryBalanceResult> InventoryBalances(Guid cid, Actor actor, DateOnly from, DateOnly to,
        Guid? branchId, Guid? warehouseId, Guid? groupId, Guid? itemId, bool showZero, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); Check.That(to >= from, "invalid_date_range", "نهاية التقرير قبل بدايته.");
        var branches = await InventoryBranches(cid, actor, branchId, warehouseId, ct);
        if (groupId is Guid group) await Required<ItemGroup>(cid, group, ct);
        if (itemId is Guid item) await Required<Item>(cid, item, ct);
        var company = await Company(cid, ct);
        // Historical movements remain visible when master cards are inactive. Names/group membership are current metadata.
        var items = (await store.Find<Item>(cid, x => (groupId == null || x.GroupId == groupId) && (itemId == null || x.Id == itemId), ct)).ToDictionary(x => x.Id);
        var itemIds = items.Keys.ToArray();
        var movements = await store.Find<StockMovement>(cid, x => branches.Contains(x.BranchId) && x.AccountingDate <= to &&
            (warehouseId == null || x.WarehouseId == warehouseId) && itemIds.Contains(x.ItemId), ct);
        var rows = movements.GroupBy(x => new { x.BranchId, x.WarehouseId, x.ItemId, x.IdentityId, x.InventoryAccountId }).Select(g =>
        {
            var before = g.Where(x => x.AccountingDate < from).ToArray(); var period = g.Where(x => x.AccountingDate >= from).ToArray();
            var card = items[g.Key.ItemId];
            return new InventoryBalanceRow(g.Key.BranchId, g.Key.WarehouseId, card.Id, card.Code, card.Name, card.BaseUnitId,
                g.Key.IdentityId, g.Key.InventoryAccountId, before.Sum(x => x.BaseQuantity), before.Sum(x => x.Value),
                period.Sum(x => Math.Max(x.BaseQuantity, 0)), period.Sum(x => Math.Max(x.Value, 0)),
                period.Sum(x => Math.Max(-x.BaseQuantity, 0)), period.Sum(x => Math.Max(-x.Value, 0)),
                g.Sum(x => x.BaseQuantity), g.Sum(x => x.Value));
        }).Where(x => showZero || x.ClosingQuantity != 0 || x.ClosingValue != 0)
          .OrderBy(x => x.ItemCode, StringComparer.Ordinal).ThenBy(x => x.ItemId).ThenBy(x => x.BranchId)
          .ThenBy(x => x.WarehouseId).ThenBy(x => x.IdentityId).ThenBy(x => x.InventoryAccountId).ToArray();
        // Quantities of unrelated items/units must never be summed as a report grand total.
        return new(cid, company.FunctionalCurrency, from, to, branches, warehouseId, groupId, itemId, showZero, true,
            rows.Sum(x => x.OpeningValue), rows.Sum(x => x.InValue), rows.Sum(x => x.OutValue), rows.Sum(x => x.ClosingValue),
            new(rows.Skip(offset).Take(limit).ToArray(), rows.Length, offset, limit));
    }
    public async Task<InventoryLedgerResult> InventoryLedger(Guid cid, Guid itemId, Actor actor, DateOnly from, DateOnly to,
        Guid? branchId, Guid? warehouseId, string? identityNumber, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); Check.That(to >= from, "invalid_date_range", "نهاية التقرير قبل بدايته.");
        var branches = await InventoryBranches(cid, actor, branchId, warehouseId, ct);
        var item = await Required<Item>(cid, itemId, ct); var company = await Company(cid, ct);
        identityNumber = string.IsNullOrWhiteSpace(identityNumber) ? null : Check.Code(identityNumber, "الدفعة أو التسلسل");
        var identities = identityNumber is null ? Array.Empty<Guid>() :
            (await store.Find<InventoryIdentity>(cid, x => x.ItemId == itemId && (x.LotNumber == identityNumber || x.SerialNumber == identityNumber), ct)).Select(x => x.Id).ToArray();
        // One immutable movement read supplies opening, full totals and page running balances.
        var movements = await store.Find<StockMovement>(cid, x => branches.Contains(x.BranchId) && x.ItemId == itemId && x.AccountingDate <= to &&
            (warehouseId == null || x.WarehouseId == warehouseId) && (identityNumber == null || x.IdentityId != null && identities.Contains(x.IdentityId.Value)), ct);
        var openingQuantity = movements.Where(x => x.AccountingDate < from).Sum(x => x.BaseQuantity);
        var openingValue = movements.Where(x => x.AccountingDate < from).Sum(x => x.Value);
        // Posting timestamp makes same-day effects explicit, including backdated documents; ID is a deterministic tie-breaker.
        var period = movements.Where(x => x.AccountingDate >= from).OrderBy(x => x.AccountingDate).ThenBy(x => x.OccurredAt)
            .ThenBy(x => x.ReversesMovementId.HasValue).ThenBy(x => x.Id).ToArray();
        var page = period.Skip(offset).Take(limit).ToArray(); var sourceIds = page.Select(x => x.OpeningId).Distinct().ToArray();
        var sources = (await store.Find<InventoryOpening>(cid, x => sourceIds.Contains(x.Id), ct)).ToDictionary(x => x.Id);
        var quantity = openingQuantity + period.Take(offset).Sum(x => x.BaseQuantity);
        var value = openingValue + period.Take(offset).Sum(x => x.Value); var rows = new List<InventoryLedgerRow>();
        foreach (var movement in page)
        {
            quantity += movement.BaseQuantity; value += movement.Value;
            var source = sources[movement.OpeningId];
            var lines = source.Lines.Where(x => x.ItemId == itemId && x.IdentityId == movement.IdentityId).OrderBy(x => x.Position).ToArray();
            var sign = movement.ReversesMovementId is null ? 1 : -1;
            rows.Add(new(movement.Id, source.Id, source.Number, movement.JournalId, movement.ReversesMovementId, movement.BranchId,
                movement.WarehouseId, movement.IdentityId, lines[0].LotNumber, lines[0].SerialNumber, movement.AccountingDate, movement.OccurredAt,
                Math.Max(movement.BaseQuantity, 0), Math.Max(-movement.BaseQuantity, 0), Math.Max(movement.Value, 0), Math.Max(-movement.Value, 0), quantity, value,
                lines.Select(x => new InventoryDocumentUnit(x.Position, x.UnitId, x.UnitCode, sign * x.Quantity, x.FactorToBase, x.UnitCost, sign * x.Value)).ToArray()));
        }
        return new(cid, itemId, item.Code, item.Name, item.BaseUnitId, company.FunctionalCurrency, from, to, branches, warehouseId, identityNumber, true,
            openingQuantity, openingValue, period.Sum(x => Math.Max(x.BaseQuantity, 0)), period.Sum(x => Math.Max(-x.BaseQuantity, 0)),
            period.Sum(x => Math.Max(x.Value, 0)), period.Sum(x => Math.Max(-x.Value, 0)),
            openingQuantity + period.Sum(x => x.BaseQuantity), openingValue + period.Sum(x => x.Value), new(rows.ToArray(), period.Length, offset, limit));
    }
}
