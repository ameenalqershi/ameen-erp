namespace Ameen.Erp.Application;
public sealed record ProcessJournalsRequest(string Action, JournalProcessingItem[] Items, string? BatchKey = null);
