namespace Ameen.Erp.Application;
public sealed record InventoryBalanceResult(Guid CompanyId, string Currency, DateOnly From, DateOnly To, Guid[] BranchIds,
    Guid? WarehouseId, Guid? GroupId, Guid? ItemId, bool ShowZero, bool InventoryOpeningsOnly,
    decimal OpeningValue, decimal InValue, decimal OutValue, decimal ClosingValue, Page<InventoryBalanceRow> Rows);
