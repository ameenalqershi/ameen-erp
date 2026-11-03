namespace Ameen.Erp.Application;

public sealed record EditCashBoxRequest(Guid ExpectedVersion, string Code, string Name, Guid AccountId,
    Guid BranchId, Guid CustodianMembershipId, string CurrencyCode, decimal? BalanceLimit,
    string? EnglishName, string? Notes, string Reason);
