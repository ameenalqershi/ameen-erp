namespace Ameen.Erp.Application;
public sealed record CreateGeneralOpeningRequest(Guid BranchId, DateOnly DocumentDate, DateOnly AccountingDate,
    DateOnly OperationStartsOn, string PreviousTrialBalanceReference, string CurrencyCode, decimal ExchangeRate,
    string Description, string? ExternalReference, JournalLineRequest[] Lines);
