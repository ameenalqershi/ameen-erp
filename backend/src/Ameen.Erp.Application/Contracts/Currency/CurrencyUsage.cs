using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CurrencyUsage(Guid CurrencyId, bool IsFunctionalCurrency, int AccountCount, int ExchangeRateCount,
    int JournalCount, int RecurringTemplateCount);
