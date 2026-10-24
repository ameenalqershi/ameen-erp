namespace Ameen.Erp.Application;
public sealed record InventoryDocumentUnit(int Position, Guid UnitId, string UnitCode, decimal Quantity,
    decimal FactorToBase, decimal UnitCost, decimal Value);
