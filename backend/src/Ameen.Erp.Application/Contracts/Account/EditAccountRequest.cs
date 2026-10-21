using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditAccountRequest(Guid ExpectedVersion, string Code, string Name, AccountType Type, Guid? ParentId,
    bool IsPosting, bool IsControl, bool RequiresCostCenter, string? CurrencyCode, NormalBalance NormalBalance,
    string? EnglishName, string? Notes, string Reason);
