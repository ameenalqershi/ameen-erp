using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class PeriodCloseService : ApplicationService
{
    public PeriodCloseService(IErpStore store, ITransactionRunner transactions, IClock clock) : base(store, transactions, clock)
    {
    }


    private async Task<IReadOnlyList<JournalEntry>> PeriodJournals(Guid cid, Guid id, CancellationToken ct) =>
        await store.Find<JournalEntry>(cid, x => x.FiscalPeriodId == id, ct);
    private static bool PendingJournal(JournalEntry x) => x.State is JournalState.Draft or JournalState.Submitted or JournalState.Approved;
    private static bool PostedJournal(JournalEntry x) => x.State is JournalState.Posted or JournalState.Reversed;
    private static string? BalanceError(JournalEntry journal)
    {
        try { journal.ValidateBalance(); return null; }
        catch (BusinessException e) { return e.Code; }
    }
    private async Task<bool> InventoryReconciles(Guid cid, Guid periodId, CancellationToken ct)
    {
        var openings = await store.Find<InventoryOpening>(cid, x => x.FiscalPeriodId == periodId && x.State == InventoryOpeningState.Posted, ct);
        foreach (var opening in openings)
        {
            var journal = opening.JournalId is Guid id ? await store.Get<JournalEntry>(cid, id, ct) : null;
            var movements = await store.Find<StockMovement>(cid, x => x.OpeningId == opening.Id, ct);
            if (!InventoryOpeningReconciliation.Matches(opening, journal, movements)) return false;
            if (opening.ReversesOpeningId is Guid sourceId)
            {
                var source = await store.Get<InventoryOpening>(cid, sourceId, ct);
                var original = source?.JournalId is Guid journalId ? await store.Get<JournalEntry>(cid, journalId, ct) : null;
                if (source is null || source.ReversesOpeningId is not null || original is null || original.State != JournalState.Reversed ||
                    original.ReversalJournalId != journal!.Id || journal.ReversesJournalId != original.Id) return false;
                var originals = await store.Find<StockMovement>(cid, x => x.OpeningId == sourceId, ct);
                if (originals.Count != movements.Count || !movements.All(m => originals.Any(o => m.ReversesMovementId == o.Id && m.ItemId == o.ItemId &&
                    m.IdentityId == o.IdentityId && m.WarehouseId == o.WarehouseId && m.BaseQuantity == -o.BaseQuantity && m.Value == -o.Value))) return false;
            }
            else if (journal!.State == JournalState.Reversed)
            {
                var opposite = journal.ReversalJournalId is Guid reversalId ? await store.Get<JournalEntry>(cid, reversalId, ct) : null;
                var reversal = opposite?.InventoryOpeningId is Guid openingId ? await store.Get<InventoryOpening>(cid, openingId, ct) : null;
                if (reversal is null || reversal.ReversesOpeningId != opening.Id || reversal.State != InventoryOpeningState.Posted || opposite!.ReversesJournalId != journal.Id) return false;
                var reverseMovements = await store.Find<StockMovement>(cid, x => x.OpeningId == reversal.Id, ct);
                if (!InventoryOpeningReconciliation.Matches(reversal, opposite, reverseMovements)) return false;
            }
        }
        var sources = await store.Find<JournalEntry>(cid, x => x.FiscalPeriodId == periodId && x.Source == JournalSource.InventoryOpening, ct);
        return sources.All(j => openings.Any(o => o.Id == j.InventoryOpeningId && o.JournalId == j.Id));
    }
    public async Task<PeriodClosePreview> PreviewPeriodClose(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.PeriodClose, ct);
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        var period = await Required<FiscalPeriod>(cid, id, ct); var company = await Company(cid, ct);
        var journals = await PeriodJournals(cid, id, ct);
        var issues = new List<PeriodCloseIssue>();
        foreach (var journal in journals.OrderBy(x => x.Number, StringComparer.Ordinal).ThenBy(x => x.Id))
        {
            if (PendingJournal(journal)) issues.Add(new(journal.Id, journal.BranchId, journal.Number, journal.State, "unposted_journal"));
            else if (PostedJournal(journal) && BalanceError(journal) is string error)
                issues.Add(new(journal.Id, journal.BranchId, journal.Number, journal.State, error));
        }
        var openings = await store.Find<InventoryOpening>(cid, x => x.FiscalPeriodId == id && x.State != InventoryOpeningState.Cancelled && x.State != InventoryOpeningState.Posted, ct);
        var checks = new List<PeriodCloseCheck>
        {
            new("inventory_openings_reconciled", await InventoryReconciles(cid, id, ct)),
            new("no_pending_inventory_openings", openings.Count == 0),
            new("company_active", company.IsActive), new("company_profile_complete", company.IsProfileComplete),
            new("period_open", !period.IsClosed), new("no_unposted_journals", !journals.Any(PendingJournal)),
            new("posted_journals_balanced", !issues.Any(x => x.Code != "unposted_journal"))
        };
        return new(id, period.Version, clock.UtcNow, true, checks.All(x => x.Passed), checks,
            journals.Count(PostedJournal), journals.Count(PendingJournal), new(issues.Skip(offset).Take(limit).ToArray(), issues.Count, offset, limit), openings.Count);
    }
    public async Task<PeriodExecutionSummary> PeriodExecutionHistory(
        Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await AuthorizeGlobal(cid, actor, Permissions.PeriodClose, ct);
        var period = await Required<FiscalPeriod>(cid, id, ct);
        var history = await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
                (x.Action == "period.closed" || x.Action == "period.reopened"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id),
            offset, limit, ct);
        return new(id, period.Code, period.IsClosed, period.Version, history);
    }

    // GL12-A05: this is a separate reopening audit surface. A user authorized
    // for closing (GL11) cannot inspect reopened exceptions without the
    // independent PeriodReopen permission, Read, Audit and all-branch scope.
    // This is an immutable view over *recorded* exceptions, not an approval
    // or extension of their validity; GL12's approval/expiry workflow remains
    // a distinct prerequisite and is never inferred from audit metadata.
    public async Task<PeriodExecutionSummary> ReopenExecutionHistory(
        Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await AuthorizeGlobal(cid, actor, Permissions.PeriodReopen, ct);
        var period = await Required<FiscalPeriod>(cid, id, ct);
        var events = await store.ReadPage<AuditEvent>(cid,
            x => x.ResourceId == id && x.Action == "period.reopened",
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id),
            offset, limit, ct);
        return new(id, period.Code, period.IsClosed, period.Version, events);
    }

    public Task<CommandReceipt> ChangePeriod(Guid cid, Guid id, Actor actor, string key, ChangePeriodRequest request, bool close, CancellationToken ct) =>
        Command(cid, actor, key, $"period.{(close ? "close" : "reopen")}:{id}", request,
            token => AuthorizeGlobal(cid, actor, close ? Permissions.PeriodClose : Permissions.PeriodReopen, token), async token =>
        {
            var period = await Required<FiscalPeriod>(cid, id, token); Check.Version(period, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب الإقفال أو الفتح", 500);
            if (close)
            {
                Check.That(!(await store.Find<InventoryOpening>(cid, x => x.FiscalPeriodId == id && x.State != InventoryOpeningState.Cancelled && x.State != InventoryOpeningState.Posted, token)).Any(),
                    "pending_inventory_openings", "الفترة تحتوي افتتاحات مخزون غير مكتملة؛ لا يمكن الإقفال.", ErrorKind.Conflict);
                var journals = await PeriodJournals(cid, id, token);
                Check.That(!journals.Any(PendingJournal), "unposted_journals", "الفترة تحتوي قيودًا غير مرحلة؛ عالجها قبل الإقفال.", ErrorKind.Conflict);
                foreach (var journal in journals.Where(PostedJournal)) journal.ValidateBalance();
                Check.That(await InventoryReconciles(cid, id, token), "inventory_opening_reconciliation_failed", "افتتاح المخزون لا يطابق القيد المرتبط؛ لا يمكن الإقفال.", ErrorKind.Conflict);
            }
            period.SetClosed(close);
            Audit(cid, actor, close ? "period.closed" : "period.reopened", period, new { reason, GeneralLedgerOnly = true });
            return Receipt(period, close ? "Closed" : "Open");
        }, ct);
}
