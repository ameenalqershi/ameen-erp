using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record TrialBalanceResult(Guid CompanyId, string Currency, DateOnly From, DateOnly To,
    IReadOnlyList<Guid> BranchIds, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalOpeningDebit,
    decimal TotalOpeningCredit, decimal TotalPeriodDebit, decimal TotalPeriodCredit, decimal TotalClosingDebit, decimal TotalClosingCredit,
    Guid? CostCenterId, int? AccountLevel, bool ShowZero);
