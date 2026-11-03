using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

/// <summary>
/// Read-only, branch-authorized references to the existing stock ledger.
/// Movement totals describe recorded history (including reversals), NOT available-to-sell stock.
/// </summary>
public sealed record InventoryResourceUsage(
    Guid ResourceId, int MovementCount, int OpeningDocumentCount, int ReversalMovementCount, bool HasStockMovements)
{
    public static InventoryResourceUsage FromMovements(Guid id, IEnumerable<StockMovement> source)
    {
        var movements = source.ToArray();
        return new(id, movements.Length, movements.Select(x => x.OpeningId).Distinct().Count(),
            movements.Count(x => x.ReversesMovementId is not null), movements.Length > 0);
    }
}
