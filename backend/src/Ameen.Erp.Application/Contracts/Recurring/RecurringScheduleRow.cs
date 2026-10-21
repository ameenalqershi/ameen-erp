using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record RecurringScheduleRow(Guid FiscalPeriodId, string PeriodCode, DateOnly DueDate, bool IsPeriodClosed,
    Guid? GeneratedJournalId, JournalState? GeneratedState);
