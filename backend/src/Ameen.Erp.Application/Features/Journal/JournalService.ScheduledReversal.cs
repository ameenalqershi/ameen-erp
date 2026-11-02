using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class JournalService
{
    // Used by the authorized run-due endpoint and the explicitly opted-in hosted worker.
    // A linked reversal history, including a cancelled draft, suppresses later scheduled regeneration.
    public async Task<ReversalDueRunResult> RunScheduledReversals(Guid cid, Actor actor, DateOnly asOf, int limit, CancellationToken ct)
    {
        Check.That(limit is >= 1 and <= 100, "invalid_schedule_limit", "حد تشغيل العكس من 1 إلى 100 قيد.");
        Check.That(asOf != DateOnly.MinValue && asOf <= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            "future_schedule_forbidden", "لا يجوز تشغيل عكس مجدول بتاريخ مستقبلي.");
        var reverseMember = await Authorize(cid, actor, Permissions.Reverse, null, ct);
        var draftMember = await Authorize(cid, actor, Permissions.Draft, null, ct);
        var reverseBranches = await AllowedBranches(cid, reverseMember, null, ct);
        var draftBranches = await AllowedBranches(cid, draftMember, null, ct);
        var branches = reverseBranches.Intersect(draftBranches).ToArray();
        var candidates = await store.Find<JournalEntry>(cid, x => x.Source == JournalSource.Manual &&
            x.State == JournalState.Posted && x.AutomaticReversal && x.AutomaticReversalDate != null &&
            x.AutomaticReversalDate <= asOf && x.PendingReversalJournalId == null && x.ReversalJournalId == null &&
            branches.Contains(x.BranchId), ct);
        var ids = candidates.Select(x => x.Id).ToArray();
        var history = await store.Find<JournalEntry>(cid, x => x.ReversesJournalId != null &&
            ids.Contains(x.ReversesJournalId!.Value), ct);
        var used = history.Select(x => x.ReversesJournalId!.Value).ToHashSet();
        var rows = new List<ReversalDueRunRow>();
        foreach (var original in candidates.Where(x => !used.Contains(x.Id))
            .OrderBy(x => x.AutomaticReversalDate).ThenBy(x => x.Id).Take(limit))
        {
            ct.ThrowIfCancellationRequested();
            var date = original.AutomaticReversalDate!.Value;
            try
            {
                // Stable request body and key make retries and worker replicas idempotent.
                var key = $"reverse-sched:{original.Id:N}";
                var receipt = await ReverseJournal(cid, original.Id, actor, key,
                    new ReverseJournalRequest(original.Version, date, "عكس تلقائي مجدول للقيد " + original.Number), ct);
                rows.Add(new(original.Id, date, receipt.ResourceId, true, null));
            }
            catch (BusinessException ex)
            {
                rows.Add(new(original.Id, date, null, false, ex.Code));
            }
        }
        return new(asOf, rows.Count, rows.Count(x => x.Succeeded), rows.Count(x => !x.Succeeded), rows.ToArray());
    }
}
