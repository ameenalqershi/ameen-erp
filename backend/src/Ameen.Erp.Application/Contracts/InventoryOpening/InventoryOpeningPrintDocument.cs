namespace Ameen.Erp.Application;

// Read-only authorized INV06 opening source as currently saved, not a new journal or posting action.
public sealed record InventoryOpeningPrintLine(
    int Position, string ItemCode, string UnitCode,
    string? LotNumber, string? SerialNumber,
    decimal Quantity, decimal FactorToBase, decimal BaseQuantity,
    decimal UnitCost, decimal Value, decimal SignedEffectValue);

public sealed record InventoryOpeningPrintDocument(
    Guid OpeningId, string Number, string CompanyName, string BranchName,
    string WarehouseCode, string WarehouseName, string State, string CurrencyCode,
    decimal ExchangeRate, DateOnly DocumentDate, DateOnly AccountingDate, DateOnly CutoffDate,
    string CountReference, string Description, string? ExternalReference,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, int Revision,
    string? ApprovedBy, string? PostedBy, DateTimeOffset? PostedAt,
    Guid? JournalId, Guid? ReversesOpeningId, string? ReversalReason,
    IReadOnlyList<InventoryOpeningPrintLine> Lines,
    decimal TotalDocumentValue, decimal SignedEffectValue);
