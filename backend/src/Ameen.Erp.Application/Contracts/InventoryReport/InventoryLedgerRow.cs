namespace Ameen.Erp.Application;
public sealed record InventoryLedgerRow(Guid MovementId, Guid OpeningId, string DocumentNumber, Guid JournalId,
    Guid? ReversesMovementId, Guid BranchId, Guid WarehouseId, Guid? IdentityId, string? LotNumber, string? SerialNumber,
    DateOnly AccountingDate, DateTimeOffset OccurredAt, decimal InQuantity, decimal OutQuantity,
    decimal InValue, decimal OutValue, decimal RunningQuantity, decimal RunningValue, InventoryDocumentUnit[] DocumentUnits);
