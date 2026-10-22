using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record PeriodClosePreview(Guid PeriodId, Guid ExpectedVersion, DateTimeOffset CheckedAt, bool GeneralLedgerOnly,
    bool CanCloseGeneralLedger, IReadOnlyList<PeriodCloseCheck> Checks, int PostedJournalCount, int UnpostedJournalCount, Page<PeriodCloseIssue> Issues, int PendingInventoryOpeningCount = 0);
