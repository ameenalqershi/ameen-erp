namespace Ameen.Erp.Application;

// Result describes draft generation only. No approval or posting is implied.
public sealed record RecurringDueRunRow(Guid TemplateId, Guid FiscalPeriodId, DateOnly DueDate,
    Guid? JournalId, bool Succeeded, string? ErrorCode);
public sealed record RecurringDueRunResult(DateOnly AsOf, int SelectedCount, int GeneratedCount,
    int FailedCount, RecurringDueRunRow[] Rows);
