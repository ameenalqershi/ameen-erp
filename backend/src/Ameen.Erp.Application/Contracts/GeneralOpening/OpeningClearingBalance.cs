namespace Ameen.Erp.Application;
public sealed record OpeningClearingBalance(Guid AccountId, decimal PostedBalance, decimal ProposedChange,
    decimal ResultingBalance, bool IsZero);
