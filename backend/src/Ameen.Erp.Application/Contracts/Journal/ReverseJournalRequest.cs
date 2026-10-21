using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record ReverseJournalRequest(Guid ExpectedVersion, DateOnly AccountingDate, string Reason);
