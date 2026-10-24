namespace Ameen.Erp.Application;
public sealed record JournalProcessingRow(Guid JournalId, bool Succeeded, bool RejectionRecorded, CommandReceipt? Receipt, string? ErrorCode);
