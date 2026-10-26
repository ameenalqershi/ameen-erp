namespace Ameen.Erp.Application;

public sealed record DraftJournalHeaderRequest(Guid BranchId, DateOnly DocumentDate,
    DateOnly AccountingDate, string? ExternalReference, DateOnly? OperationStartsOn = null);
