using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record AccountLedgerResult(Guid AccountId, string Currency, DateOnly From, DateOnly To,
    decimal OpeningBalance, decimal ClosingBalance, decimal PeriodDebit, decimal PeriodCredit, Page<LedgerRow> Entries,
    Guid CompanyId, IReadOnlyList<Guid> BranchIds, Guid? CostCenterId, string? ExternalReference);
