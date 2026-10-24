namespace Ameen.Erp.Application;
public sealed record JournalProcessingItem(Guid JournalId, Guid ExpectedVersion, string IdempotencyKey, string? Reason = null);
