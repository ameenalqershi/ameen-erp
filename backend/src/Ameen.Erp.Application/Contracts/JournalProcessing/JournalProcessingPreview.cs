namespace Ameen.Erp.Application;
public sealed record JournalProcessingPreview(JournalSummary Journal, string Action, bool CanExecute, string? ErrorCode, DateTimeOffset CheckedAt);
