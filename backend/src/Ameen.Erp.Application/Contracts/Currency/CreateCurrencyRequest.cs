using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateCurrencyRequest(string Code, int Scale, string Name, string? EnglishName = null,
    string? Notes = null, string? DisplaySymbol = null, string? FractionName = null);
