using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

// Read-only snapshot of the proposed journal effect, never a posting or approval.
public sealed record JournalImpactPreview(
    Guid JournalId, Guid Version, string Number, JournalState State, JournalSource Source,
    Guid BranchId, DateOnly AccountingDate, string FunctionalCurrency,
    decimal FunctionalDebit, decimal FunctionalCredit, decimal Difference,
    bool IsBalanced, bool CanSubmit, string? ValidationErrorCode,
    JournalImpactLine[] Lines);

public sealed record JournalImpactLine(
    int Position, Guid AccountId, Guid? CostCenterId, string CurrencyCode,
    Guid? ExchangeRateId, decimal ExchangeRate, decimal Debit, decimal Credit,
    decimal FunctionalDebit, decimal FunctionalCredit, decimal FunctionalNet);
