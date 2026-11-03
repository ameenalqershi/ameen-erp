namespace Ameen.Erp.Application;

// GL03/GL07: print only actual manual or reversal journal state.
// Source and per-line currency/rate are taken from the saved historical ledger.
public sealed record JournalPrintDocument(
    Guid JournalId, string Number, string CompanyName, string BranchName,
    string Source, string State, string FunctionalCurrency,
    DateOnly DocumentDate, DateOnly AccountingDate,
    string Description, string? ExternalReference,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset LastModifiedAt,
    int Revision, DateTimeOffset? PostedAt,
    string? OriginalJournalNumber, string? ReversalReason,
    IReadOnlyList<JournalPrintLine> Lines,
    string? OriginalTotalsCurrency, decimal? TotalDebit, decimal? TotalCredit,
    decimal FunctionalDebit, decimal FunctionalCredit);
