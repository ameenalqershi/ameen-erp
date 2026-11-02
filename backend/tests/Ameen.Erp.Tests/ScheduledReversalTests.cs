using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class ScheduledReversalTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-F14/F15: conditional reversal fields validate, persist, edit and disable", async f =>
        {
            await Error("reversal_date_required", () => f.Draft(f.Request() with { AutomaticReversal = true }));
            await Error("unexpected_reversal_date", () => f.Draft(f.Request() with { ReversalDate = Fixture.Date.AddDays(1) }));
            await Error("reversal_before_original", () => f.Draft(f.Request() with { AutomaticReversal = true, ReversalDate = Fixture.Date.AddDays(-1) }));
            var created = await f.Draft(f.Request() with { AutomaticReversal = true, ReversalDate = Fixture.Date.AddDays(2) });
            var journal = await f.Journal(created.ResourceId);
            Ensure(journal.AutomaticReversal && journal.AutomaticReversalDate == Fixture.Date.AddDays(2));
            var edited = await f.Service.Journal.EditJournal(f.CompanyId, journal.Id, f.Clerk, Fixture.Key(),
                new(journal.Version, "تعديل موعد العكس", f.Request().Lines, AutomaticReversal: true, ReversalDate: Fixture.Date.AddDays(3)), default);
            journal = await f.Journal(created.ResourceId);
            Ensure(journal.AutomaticReversalDate == Fixture.Date.AddDays(3) && edited.Version == journal.Version);
            edited = await f.Service.Journal.EditJournal(f.CompanyId, journal.Id, f.Clerk, Fixture.Key(),
                new(journal.Version, "تعطيل العكس", f.Request().Lines, AutomaticReversal: false), default);
            journal = await f.Journal(created.ResourceId);
            Ensure(!journal.AutomaticReversal && journal.AutomaticReversalDate is null);
            var summary = (await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default)).Items.Single();
            Ensure(!summary.AutomaticReversal && summary.AutomaticReversalDate is null);
        });
        test("GL03-GP05: scheduled run creates one due historical inverse draft only", async f =>
        {
            var original = await f.Post(await f.Draft(f.Request(date: Fixture.Date.AddDays(-1)) with { AutomaticReversal = true, ReversalDate = Fixture.Date }));
            var early = await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date.AddDays(-1), 100, default);
            Ensure(early.SelectedCount == 0);
            var outbox = f.Store.Count<OutboxEvent>();
            var result = await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default);
            Ensure(result.SelectedCount == 1 && result.GeneratedCount == 1 && result.FailedCount == 0);
            var reversal = await f.Journal(result.Rows.Single().ReversalJournalId!.Value);
            Ensure(reversal.State == JournalState.Draft && reversal.Source == JournalSource.Reversal &&
                reversal.ReversesJournalId == original.ResourceId && reversal.AccountingDate == Fixture.Date &&
                reversal.ApprovedBy is null && reversal.PostedAt is null && !reversal.AutomaticReversal &&
                reversal.Lines.Single(x => x.AccountId == f.CashId).FunctionalCredit == 100);
            Ensure(f.Store.Count<OutboxEvent>() == outbox &&
                (await f.Journal(original.ResourceId)).PendingReversalJournalId == reversal.Id);
            Ensure((await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default)).SelectedCount == 0);
        });
        test("GL03-GP05: cancelled scheduled draft remains history and cannot regenerate", async f =>
        {
            var original = await f.Post(await f.Draft(f.Request() with { AutomaticReversal = true, ReversalDate = Fixture.Date }));
            var run = await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default);
            var draft = await f.Journal(run.Rows.Single().ReversalJournalId!.Value);
            await f.Action(new CommandReceipt(draft.Id, draft.Version, draft.State.ToString(), draft.Number), "cancel", reason: "إلغاء مسودة العكس");
            var fresh = await f.Journal(original.ResourceId);
            Ensure(fresh.PendingReversalJournalId is null && fresh.State == JournalState.Posted);
            Ensure((await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default)).SelectedCount == 0);
            var manual = await f.Service.Journal.ReverseJournal(f.CompanyId, fresh.Id, f.Clerk, Fixture.Key(),
                new(fresh.Version, Fixture.Date, "عكس يدوي بعد الإلغاء"), default);
            Ensure(manual.Status == "Draft");
        });
        test("GL03-GP05: one failed due journal does not block another tenant-scoped draft", async f =>
        {
            var september = await f.Post(await f.Draft(f.Request(date: new(2026, 9, 15)) with { AutomaticReversal = true, ReversalDate = new(2026, 9, 20) }));
            var october = await f.Post(await f.Draft(f.Request() with { AutomaticReversal = true, ReversalDate = Fixture.Date }));
            var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Month == 9);
            await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default);
            var result = await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default);
            Ensure(result.SelectedCount == 2 && result.GeneratedCount == 1 && result.FailedCount == 1 &&
                result.Rows.Single(x => x.OriginalJournalId == september.ResourceId).ErrorCode == "period_closed" &&
                result.Rows.Single(x => x.OriginalJournalId == october.ResourceId).Succeeded);
            await Error("future_schedule_forbidden", () => f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, DateOnly.MaxValue, 100, default));
            await Error("invalid_schedule_limit", () => f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 0, default));
        });
        test("GL03-GP05: reverse and draft rights and branch scopes are independently enforced", async f =>
        {
            var original = await f.Post(await f.Draft(f.Request(branch: f.OtherBranchId) with { AutomaticReversal = true, ReversalDate = Fixture.Date }));
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(f.Limited.Subject, new[] { Permissions.Read, Permissions.Draft, Permissions.Reverse }, false, new[] { f.BranchId }, member.Version), default);
            Ensure((await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Limited, Fixture.Date, 100, default)).SelectedCount == 0);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(f.Clerk.Subject, new[] { Permissions.Read, Permissions.Draft }, true, Array.Empty<Guid>(),
                    f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject).Version), default);
            await Error("permission_denied", () => f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default));
        });
        test("GL03-GP05: failed reversal commit leaves no reservation, number, audit or receipt", async f =>
        {
            var original = await f.Post(await f.Draft(f.Request() with { AutomaticReversal = true, ReversalDate = Fixture.Date }));
            var audits = f.Store.Count<AuditEvent>(); var receipts = f.Store.Count<IdempotencyRecord>();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default); throw new Exception("Expected injected commit failure"); }
            catch (IOException) { }
            var fresh = await f.Journal(original.ResourceId);
            Ensure(fresh.PendingReversalJournalId is null && f.Store.Count<JournalEntry>() == 1 &&
                f.Store.Count<AuditEvent>() == audits && f.Store.Count<IdempotencyRecord>() == receipts);
            var retry = await f.Service.Journal.RunScheduledReversals(f.CompanyId, f.Clerk, Fixture.Date, 100, default);
            Ensure(retry.GeneratedCount == 1 && f.Store.Count<JournalEntry>() == 2);
        });
    }

    private static void Ensure(bool value) { if (!value) throw new Exception("Scheduled reversal assertion failed"); }
    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected business error " + code);
    }
}
