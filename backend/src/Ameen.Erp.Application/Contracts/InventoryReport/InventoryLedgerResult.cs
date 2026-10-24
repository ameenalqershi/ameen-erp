namespace Ameen.Erp.Application;
public sealed record InventoryLedgerResult(Guid CompanyId, Guid ItemId, string ItemCode, string ItemName, Guid BaseUnitId,
    string Currency, DateOnly From, DateOnly To, Guid[] BranchIds, Guid? WarehouseId, string? IdentityNumber,
    bool InventoryOpeningsOnly, decimal OpeningQuantity, decimal OpeningValue, decimal InQuantity, decimal OutQuantity,
    decimal InValue, decimal OutValue, decimal ClosingQuantity, decimal ClosingValue, Page<InventoryLedgerRow> Rows);
