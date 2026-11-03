namespace Ameen.Erp.Application;
public sealed record JournalProcessingDurableResult(Guid BatchId, Guid Version, int Revision,
    string Action, DateTimeOffset CreatedAt, DateTimeOffset LastModifiedAt, DateTimeOffset? FinishedAt,
    bool AllSucceeded, int SucceededCount, int FailedCount, JournalProcessingRow[] Rows);
