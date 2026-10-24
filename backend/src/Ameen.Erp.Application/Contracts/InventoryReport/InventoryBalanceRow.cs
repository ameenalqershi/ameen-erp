namespace Ameen.Erp.Application;
public sealed record InventoryBalanceRow(Guid BranchId, Guid WarehouseId, Guid ItemId, string ItemCode, string ItemName,
    Guid BaseUnitId, Guid? IdentityId, Guid InventoryAccountId, decimal OpeningQuantity, decimal OpeningValue,
    decimal InQuantity, decimal InValue, decimal OutQuantity, decimal OutValue, decimal ClosingQuantity, decimal ClosingValue);
