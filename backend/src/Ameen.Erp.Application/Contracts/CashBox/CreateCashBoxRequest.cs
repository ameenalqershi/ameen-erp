namespace Ameen.Erp.Application;

public sealed record CreateCashBoxRequest(string Code, string Name, Guid AccountId, Guid BranchId,
    Guid CustodianMembershipId, string CurrencyCode, decimal? BalanceLimit = null,
    string? EnglishName = null, string? Notes = null);
