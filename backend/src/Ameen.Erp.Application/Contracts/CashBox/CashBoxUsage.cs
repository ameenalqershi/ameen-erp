namespace Ameen.Erp.Application;

// Counts existing journal references to the linked account/branch/currency.
// This is NOT the cash-box available balance and does not represent CASH03/04.
public sealed record CashBoxUsage(Guid CashBoxId, int RelatedJournalCount, bool HasRelatedJournals);
