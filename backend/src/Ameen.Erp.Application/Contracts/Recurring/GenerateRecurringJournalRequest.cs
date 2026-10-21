using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record GenerateRecurringJournalRequest(Guid ExpectedVersion, Guid FiscalPeriodId);
