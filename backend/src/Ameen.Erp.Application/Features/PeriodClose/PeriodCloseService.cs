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
        var openings = await store.Find<InventoryOpening>(cid, x => x.FiscalPeriodId == id && x.State != InventoryOpeningState.Cancelled, ct);
        var checks = new List<PeriodCloseCheck>
        {
            new("no_pending_inventory_openings", openings.Count == 0),
            new("company_active", company.IsActive), new("company_profile_complete", company.IsProfileComplete),
            new("period_open", !period.IsClosed), new("no_unposted_journals", !journals.Any(PendingJournal)),
            new("posted_journals_balanced", !issues.Any(x => x.Code != "unposted_journal"))
        };
        return new(id, period.Version, clock.UtcNow, true, checks.All(x => x.Passed), checks,
            journals.Count(PostedJournal), journals.Count(PendingJournal), new(issues.Skip(offset).Take(limit).ToArray(), issues.Count, offset, limit), openings.Count);
    }
    public Task<CommandReceipt> ChangePeriod(Guid cid, Guid id, Actor actor, string key, ChangePeriodRequest request, bool close, CancellationToken ct) =>
        Command(cid, actor, key, $"period.{(close ? "close" : "reopen")}:{id}", request,
            token => AuthorizeGlobal(cid, actor, close ? Permissions.PeriodClose : Permissions.PeriodReopen, token), async token =>
        {
            var period = await Required<FiscalPeriod>(cid, id, token); Check.Version(period, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب الإقفال أو الفتح", 500);
            if (close)
            {
                Check.That(!(await store.Find<InventoryOpening>(cid, x => x.FiscalPeriodId == id && x.State != InventoryOpeningState.Cancelled, token)).Any(),
                    "pending_inventory_openings", "الفترة تحتوي افتتاحات مخزون غير مكتملة؛ لا يمكن الإقفال.", ErrorKind.Conflict);
                var journals = await PeriodJournals(cid, id, token);
                Check.That(!journals.Any(PendingJournal), "unposted_journals", "الفترة تحتوي قيودًا غير مرحلة؛ عالجها قبل الإقفال.", ErrorKind.Conflict);
                foreach (var journal in journals.Where(PostedJournal)) journal.ValidateBalance();
            }
            period.SetClosed(close);
            Audit(cid, actor, close ? "period.closed" : "period.reopened", period, new { reason, GeneralLedgerOnly = true });
            return Receipt(period, close ? "Closed" : "Open");
        }, ct);
}
