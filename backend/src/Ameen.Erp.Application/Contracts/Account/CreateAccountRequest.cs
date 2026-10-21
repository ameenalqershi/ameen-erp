using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateAccountRequest(string Code, string Name, AccountType Type, Guid? ParentId,
    bool IsPosting, bool IsControl, bool RequiresCostCenter, string? CurrencyCode, NormalBalance NormalBalance,
    string? EnglishName = null, string? Notes = null);
