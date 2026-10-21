using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record ExchangeRateQuote(Guid CompanyId, string CurrencyCode, string FunctionalCurrency, DateOnly AccountingDate,
    Guid? ExchangeRateId, DateOnly? EffectiveDate, decimal RateToFunctional, string? Source, bool IsFunctionalCurrency);
