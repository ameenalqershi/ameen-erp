namespace Ameen.Erp.Application;
public sealed record InventoryOpeningPostingPlan(Guid SettingsVersion, Guid WarehouseVersion, Guid InventoryAccountId,
    Guid ClearingAccountId, decimal Debit, decimal Credit);
