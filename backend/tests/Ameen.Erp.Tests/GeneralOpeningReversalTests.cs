using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class GeneralOpeningReversalTests
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new FixedClock().UtcNow.AddMinutes(1);
    }

    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL02: reversal reserve cancel and post advance original metadata once with audit", async f =>
        {
            var source = await Opening(f);
            var clock = new MutableClock(); var service = new TestServices(f.Store, f.Store, clock).Journal;
            var key = Fixture.Key(); var request = new ReverseJournalRequest(source.Version, Fixture.Date, "تصحيح الافتتاح");
            var inverse = await service.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, key, request, default);
            var reserved = await f.Journal(source.ResourceId);
            Ensure(reserved.OpeningRevision == 5 && reserved.OpeningModifiedAt == clock.UtcNow && reserved.PendingReversalJournalId == inverse.ResourceId);
            var reservedVersion = reserved.Version;
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            Ensure(inverse == await service.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, key, request, default));
            Ensure((await f.Journal(source.ResourceId)).Version == reservedVersion);

            var cancelKey = Fixture.Key(); var cancel = new TransitionRequest(inverse.Version, "إلغاء التصحيح");
            await service.TransitionJournal(f.CompanyId, inverse.ResourceId, f.Clerk, cancelKey, cancel, "cancel", default);
            var released = await f.Journal(source.ResourceId);
            Ensure(released.OpeningRevision == 6 && released.OpeningModifiedAt == clock.UtcNow && released.PendingReversalJournalId == null);
            var releasedVersion = released.Version;
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            await service.TransitionJournal(f.CompanyId, inverse.ResourceId, f.Clerk, cancelKey, cancel, "cancel", default);
            Ensure((await f.Journal(source.ResourceId)).Version == releasedVersion);
            await Error("version_conflict", () => service.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, Fixture.Key(), request, default));

            inverse = await service.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, Fixture.Key(), new(releasedVersion, Fixture.Date, "تصحيح بديل"), default);
            Ensure((await f.Journal(source.ResourceId)).OpeningRevision == 7);
            inverse = await f.Action(inverse, "submit"); inverse = await f.Action(inverse, "approve", f.Approver);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var postKey = Fixture.Key(); var post = new TransitionRequest(inverse.Version);
            var receipt = await service.TransitionJournal(f.CompanyId, inverse.ResourceId, f.Clerk, postKey, post, "post", default);
            var completed = await f.Journal(source.ResourceId);
            Ensure(completed.OpeningRevision == 8 && completed.OpeningModifiedAt == clock.UtcNow && completed.State == JournalState.Reversed);
            Ensure(completed.PreviousTrialBalanceReference == "TB-2026" && completed.AccountingDate == Fixture.Date && completed.Lines.Sum(x => x.Debit) == 100);
            var version = completed.Version;
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            Ensure(receipt == await service.TransitionJournal(f.CompanyId, inverse.ResourceId, f.Clerk, postKey, post, "post", default));
            Ensure((await f.Journal(source.ResourceId)).Version == version);
            var audits = f.Store.All<AuditEvent>().Where(x => x.ResourceId == source.ResourceId && x.Action.StartsWith("general-opening.reversal-", StringComparison.Ordinal)).ToArray();
            Ensure(audits.Length == 4 && audits.Count(x => x.Action.EndsWith("reserved", StringComparison.Ordinal)) == 2);
        });

        test("GL02: failed reversal reservation rolls back original metadata audit and idempotency", async f =>
        {
            var source = await Opening(f); var original = await f.Journal(source.ResourceId);
            var revision = original.OpeningRevision; var modified = original.OpeningModifiedAt;
            var auditCount = f.Store.Count<AuditEvent>(); var key = Fixture.Key();
            var request = new ReverseJournalRequest(source.Version, Fixture.Date, "تصحيح");
            f.Store.FailBeforeCommit = true;
            await Failure(() => f.Service.Journal.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, key, request, default));
            original = await f.Journal(source.ResourceId);
            Ensure(original.Version == source.Version && original.OpeningRevision == revision && original.OpeningModifiedAt == modified && original.PendingReversalJournalId == null);
            Ensure(f.Store.Count<JournalEntry>() == 1 && f.Store.Count<AuditEvent>() == auditCount);
            await f.Service.Journal.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, key, request, default);
            Ensure((await f.Journal(source.ResourceId)).OpeningRevision == revision + 1);
        });

        test("GL02: failed cancellation keeps reversal reservation and metadata retryable", async f =>
        {
            var source = await Opening(f);
            var inverse = await f.Service.Journal.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, Fixture.Key(), new(source.Version, Fixture.Date, "تصحيح"), default);
            var original = await f.Journal(source.ResourceId); var version = original.Version; var modified = original.OpeningModifiedAt;
            var key = Fixture.Key(); var audits = f.Store.Count<AuditEvent>();
            f.Store.FailBeforeCommit = true;
            await Failure(() => f.Action(inverse, "cancel", key: key, reason: "إلغاء"));
            original = await f.Journal(source.ResourceId);
            Ensure(original.Version == version && original.OpeningRevision == 5 && original.OpeningModifiedAt == modified && original.PendingReversalJournalId == inverse.ResourceId);
            Ensure((await f.Journal(inverse.ResourceId)).State == JournalState.Draft && f.Store.Count<AuditEvent>() == audits);
            await f.Action(inverse, "cancel", key: key, reason: "إلغاء");
            Ensure((await f.Journal(source.ResourceId)).OpeningRevision == 6);
        });

        test("GL02: failed reversal posting restores original and no duplicate effect on retry", async f =>
        {
            var source = await Opening(f);
            var inverse = await f.Service.Journal.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, Fixture.Key(), new(source.Version, Fixture.Date, "تصحيح"), default);
            inverse = await f.Action(inverse, "submit"); inverse = await f.Action(inverse, "approve", f.Approver);
            var original = await f.Journal(source.ResourceId); var version = original.Version;
            var audits = f.Store.Count<AuditEvent>(); var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            await Failure(() => f.Action(inverse, "post", key: key));
            original = await f.Journal(source.ResourceId);
            Ensure(original.Version == version && original.OpeningRevision == 5 && original.State == JournalState.Posted && original.ReversalJournalId == null);
            Ensure((await f.Journal(inverse.ResourceId)).State == JournalState.Approved && f.Store.Count<OutboxEvent>() == 1 && f.Store.Count<AuditEvent>() == audits);
            var receipt = await f.Action(inverse, "post", key: key);
            Ensure(receipt == await f.Action(inverse, "post", key: key));
            Ensure((await f.Journal(source.ResourceId)).OpeningRevision == 6 && f.Store.Count<OutboxEvent>() == 2);
        });
    }

    private static async Task<CommandReceipt> Opening(Fixture f) => await f.Post(await f.Service.Journal.CreateGeneralOpening(
        f.CompanyId, f.Clerk, Fixture.Key(), GeneralOpeningTests.Request(f.Request()), default));
    private static void Ensure(bool value) { if (!value) throw new Exception("Opening reversal tracking assertion failed"); }
    private static async Task Failure(Func<Task> action)
    { try { await action(); } catch (IOException) { return; } throw new Exception("Expected persistence failure"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
