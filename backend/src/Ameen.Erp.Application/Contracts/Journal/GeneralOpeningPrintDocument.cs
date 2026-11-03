namespace Ameen.Erp.Application;

// Read-only, current authorized snapshot for GL02-A07 printing.
// No new financial state or document numbering is created by this contract.
public sealed record JournalPrintLine(
    int Position, string AccountCode, string AccountName, string? CostCenterCode,
    string Description, string CurrencyCode, decimal ExchangeRate,
    decimal Debit, decimal Credit, decimal FunctionalDebit, decimal FunctionalCredit);

public sealed record GeneralOpeningPrintDocument(
    Guid JournalId, string Number, string CompanyName, string BranchName,
    string State, string FunctionalCurrency, string OpeningCurrency,
    decimal OpeningExchangeRate, DateOnly DocumentDate, DateOnly AccountingDate,
    DateOnly OperationStartsOn, string PreviousTrialBalanceReference,
    string Description, string? ExternalReference,
    string CreatedBy, DateTimeOffset CreatedAt, int Revision,
    IReadOnlyList<JournalPrintLine> Lines,
    decimal TotalDebit, decimal TotalCredit, decimal FunctionalDebit, decimal FunctionalCredit);
