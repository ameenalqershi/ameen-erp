namespace Ameen.Erp.Application;
public sealed record JournalProcessingResult(string Action, DateTimeOffset FinishedAt, bool AllSucceeded, int SucceededCount, int FailedCount, JournalProcessingRow[] Rows);
