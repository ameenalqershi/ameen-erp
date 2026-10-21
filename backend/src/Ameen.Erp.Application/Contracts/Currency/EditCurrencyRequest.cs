using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditCurrencyRequest(Guid ExpectedVersion, int Scale, string Name, string? EnglishName,
    string? Notes, string? DisplaySymbol, string? FractionName, string Reason);
