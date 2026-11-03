using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class InventoryOpeningService
{
    /// <summary>
    /// A read-only print projection from the existing INV06 aggregate. Does not post, recalculate
    /// FX, allocate a journal number or allow a user to bypass the saved branch.
    /// </summary>
    public async Task<InventoryOpeningPrintDocument> Print(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var source = await Get(cid, id, actor, ct); // tenant + membership + source branch
        var company = await Company(cid, ct);
        var branch = await Required<Branch>(cid, source.BranchId, ct);
        var warehouse = await Required<Warehouse>(cid, source.WarehouseId, ct);
        var sign = source.ReversesOpeningId is null ? 1m : -1m;
        var lines = source.Lines.OrderBy(x => x.Position)
            .Select(x => new InventoryOpeningPrintLine(x.Position, x.ItemCode, x.UnitCode,
                x.LotNumber, x.SerialNumber, x.Quantity, x.FactorToBase, x.BaseQuantity,
                x.UnitCost, x.Value, sign * x.Value))
            .ToArray();
        var value = lines.Sum(x => x.Value);
        return new(source.Id, source.Number, company.Name, branch.Name, warehouse.Code,
            warehouse.Name, source.State.ToString(), source.CurrencyCode, source.ExchangeRate,
            source.DocumentDate, source.AccountingDate, source.CutoffDate,
            source.CountReference, source.Description, source.ExternalReference,
            source.CreatedBy, source.CreatedAt, source.ModifiedAt, source.Revision,
            source.ApprovedBy, source.PostedBy, source.PostedAt,
            source.JournalId, source.ReversesOpeningId, source.ReversalReason,
            lines, value, sign * value);
    }
}
