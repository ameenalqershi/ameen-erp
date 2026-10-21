using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
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
        var checks = new List<PeriodCloseCheck>
        {
            new("company_active", company.IsActive), new("company_profile_complete", company.IsProfileComplete),
            new("period_open", !period.IsClosed), new("no_unposted_journals", !journals.Any(PendingJournal)),
            new("posted_journals_balanced", !issues.Any(x => x.Code != "unposted_journal"))
        };
        return new(id, period.Version, clock.UtcNow, true, checks.All(x => x.Passed), checks,
            journals.Count(PostedJournal), journals.Count(PendingJournal), new(issues.Skip(offset).Take(limit).ToArray(), issues.Count, offset, limit));
    }
}
