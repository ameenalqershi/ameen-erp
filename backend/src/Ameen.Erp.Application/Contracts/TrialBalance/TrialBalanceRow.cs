using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record TrialBalanceRow(Guid AccountId, string Code, string Name, decimal OpeningDebit,
    decimal OpeningCredit, decimal PeriodDebit, decimal PeriodCredit, decimal ClosingDebit, decimal ClosingCredit,
    Guid? ParentId, int Level, bool IsPosting);
