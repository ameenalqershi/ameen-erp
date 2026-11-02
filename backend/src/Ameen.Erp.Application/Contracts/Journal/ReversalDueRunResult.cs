namespace Ameen.Erp.Application;

// A scheduled run reserves a linked historical draft only; approval and posting remain separate.
public sealed record ReversalDueRunRow(Guid OriginalJournalId, DateOnly ReversalDate,
    Guid? ReversalJournalId, bool Succeeded, string? ErrorCode);
public sealed record ReversalDueRunResult(DateOnly AsOf, int SelectedCount, int GeneratedCount,
    int FailedCount, ReversalDueRunRow[] Rows);
