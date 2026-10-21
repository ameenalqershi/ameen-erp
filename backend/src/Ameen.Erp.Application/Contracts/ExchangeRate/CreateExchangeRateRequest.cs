using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateExchangeRateRequest(string CurrencyCode, DateOnly EffectiveDate, decimal RateToFunctional, string Source);
