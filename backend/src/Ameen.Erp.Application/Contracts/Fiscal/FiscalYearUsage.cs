using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record FiscalYearUsage(Guid FiscalYearId, int JournalCount, int UnpostedJournalCount, int RecurringGenerationCount);
