using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL05-A05: results and audit history remain owner- and tenant-scoped,
// paginated, replay-safe, and strictly read-only.
public static class JournalReviewHistoryAcceptance
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL05-A05: mixed review decision history paginates without writes or disclosure", async f =>
        {
            var entry = await f.Draft();
            var submitted = await f.Action(entry, "submit");
            var acceptedKey = Fixture.Key();
            var approved = await f.Service.Journal.ProcessJournals(f.CompanyId, f.Approver,
                new("approve", new[] { new JournalProcessingItem(submitted.ResourceId,
                    submitted.Version, acceptedKey) }), default);
            Ensure(approved.AllSucceeded && approved.SucceededCount == 1 &&
                approved.Rows.Single().Receipt is not null, "Review approve result is not explicit");

            // A second action on the now-approved document is a recorded rejection,
            // not a second approval or a fabricated posted financial effect.
            var declinedKey = Fixture.Key();
            var declined = await f.Service.Journal.ProcessJournals(f.CompanyId, f.Approver,
                new("approve", new[] { new JournalProcessingItem(submitted.ResourceId,
                    submitted.Version, declinedKey) }), default);
            Ensure(!declined.AllSucceeded && declined.FailedCount == 1 &&
                declined.Rows.Single().RejectionRecorded && !declined.Rows.Single().Succeeded &&
                declined.Rows.Single().ErrorCode == "version_conflict",
                "Rejected review was counted as accepted");

            var auditBefore = f.Store.Count<AuditEvent>();
            var receiptBefore = f.Store.Count<IdempotencyRecord>();
            var sequencesBefore = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            var page = await f.Service.Journal.ProcessingHistory(f.CompanyId, submitted.ResourceId,
                f.Owner, 0, 1, default);
            var secondPage = await f.Service.Journal.ProcessingHistory(f.CompanyId,
                submitted.ResourceId, f.Owner, 1, 1, default);
            var empty = await f.Service.Journal.ProcessingHistory(f.CompanyId,
                submitted.ResourceId, f.Owner, 2, 1, default);
            var actions = page.Items.Concat(secondPage.Items).Select(x => x.Action).ToArray();
            Ensure(page.TotalCount == 2 && page.Items.Count == 1 &&
                secondPage.TotalCount == 2 && secondPage.Items.Count == 1 &&
                page.Items[0].Id != secondPage.Items[0].Id &&
                empty.TotalCount == 2 && empty.Items.Count == 0 &&
                actions.Contains("journal.processing-succeeded") &&
                actions.Contains("journal.processing-rejected"),
                "GL05 history omitted, duplicated or reordered results");
            await Reject("permission_denied", () => f.Service.Journal.ProcessingHistory(
                f.CompanyId, submitted.ResourceId, f.Clerk, 0, 10, default));
            Ensure(f.Store.Count<AuditEvent>() == auditBefore &&
                f.Store.Count<IdempotencyRecord>() == receiptBefore &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == sequencesBefore &&
                f.Store.Count<OutboxEvent>() == 0 &&
                (await f.Journal(submitted.ResourceId)).State == JournalState.Approved,
                "GL05 result/history reads changed accounting or audit data");
        });
    }

    public static async Task RunSql(string connection, Guid cid, Actor clerk, Actor approver,
        Actor owner, CreateJournalRequest sample)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> run)
        {
            await using var db = Db();
            return await run(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        var created = await Call(s => s.Journal.CreateJournal(cid, clerk,
            Fixture.Key(), sample, default));
        var submitted = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, clerk, Fixture.Key(), new(created.Version), "submit", default));
        async Task<JournalProcessingResult> Approve(string key) =>
            await Call(s => s.Journal.ProcessJournals(cid, approver, new("approve",
                new[] { new JournalProcessingItem(created.ResourceId, submitted.Version, key) }),
                default));
        var acceptedKey = Fixture.Key();
        var result = await Approve(acceptedKey);
        var replay = await Approve(acceptedKey);
        Ensure(result.AllSucceeded && result.Rows.SequenceEqual(replay.Rows),
            "GL05 SQL approval replay changed decision");
        var rejected = await Approve(Fixture.Key());
        Ensure(!rejected.AllSucceeded && rejected.Rows.Single().RejectionRecorded &&
            rejected.Rows.Single().ErrorCode == "version_conflict",
            "GL05 SQL rejected second approval did not create a decision");

        // SQL test fixtures give the clerk Permissions.All. Create an explicit
        // member with Read but not Audit to exercise the real authorization gate.
        var readOnly = new Actor("gl05-sql-readonly");
        await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(),
            new(readOnly.Subject, new[] { Permissions.Read }, true, Array.Empty<Guid>(), null),
            default));
        int audits, receipts, outbox;
        long seq;
        await using (var db = Db())
        {
            audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            seq = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                .SumAsync(x => x.LastNumber);
        }
        var history = await Call(s => s.Journal.ProcessingHistory(cid, created.ResourceId,
            owner, 0, 10, default));
        var p1 = await Call(s => s.Journal.ProcessingHistory(cid, created.ResourceId,
            owner, 0, 1, default));
        var p2 = await Call(s => s.Journal.ProcessingHistory(cid, created.ResourceId,
            owner, 1, 1, default));
        Ensure(history.TotalCount == 2 && history.Items.Count == 2 &&
            history.Items.Select(x => x.Action).OrderBy(x => x).SequenceEqual(
                new[] { "journal.processing-rejected", "journal.processing-succeeded" }) &&
            p1.Items.Count == 1 && p2.Items.Count == 1 &&
            p1.Items[0].Id != p2.Items[0].Id,
            "GL05 SQL decision history not queryable/paginated");
        await Reject("permission_denied", () => Call(s =>
            s.Journal.ProcessingHistory(cid, created.ResourceId, readOnly, 0, 10, default)));
        await using (var db = Db())
        {
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber) == seq &&
                await db.Set<JournalEntry>().AnyAsync(x => x.CompanyId == cid &&
                    x.Id == created.ResourceId && x.State == JournalState.Approved),
                "GL05 SQL history read mutated posted data/audit/receipt/number");
        }
        Console.WriteLine("  SQL PASS GL05-A05 mixed approval result/history, replay, paging and permission-scope without write side effects");
    }
    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    private static void Ensure(bool value, string detail)
    {
        if (!value) throw new Exception(detail);
    }
}
