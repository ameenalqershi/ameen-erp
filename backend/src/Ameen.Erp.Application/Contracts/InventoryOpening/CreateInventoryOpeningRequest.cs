namespace Ameen.Erp.Application;
public sealed record CreateInventoryOpeningRequest(Guid BranchId, Guid WarehouseId, DateOnly DocumentDate,
    DateOnly AccountingDate, DateOnly CutoffDate, string CurrencyCode, decimal ExchangeRate, string CountReference,
    string Description, InventoryOpeningLineRequest[] Lines, string? ExternalReference = null);
