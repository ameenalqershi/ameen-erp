using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CompanyUsage(Guid CompanyId, int BranchCount, int AccountCount, int CurrencyCount, int FiscalYearCount,
    int JournalCount, int RecurringTemplateCount, int ExchangeRateCount, bool CanChangeFunctionalCurrency);
