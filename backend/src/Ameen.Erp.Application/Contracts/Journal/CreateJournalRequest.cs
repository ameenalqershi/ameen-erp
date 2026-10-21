using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateJournalRequest(Guid BranchId, DateOnly DocumentDate, DateOnly AccountingDate,
    string Description, string? ExternalReference, JournalLineRequest[] Lines);
