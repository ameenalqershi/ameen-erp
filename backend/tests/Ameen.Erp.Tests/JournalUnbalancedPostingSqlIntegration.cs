using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// AT01: even an already-approved document must fail closed if persisted ledger
// lines no longer balance. Only used in SqlIntegration's disposable database.
public static class JournalUnbalancedPostingSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor clerk, Actor approver,
        CreateJournalRequest example)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        var draft = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(),
            example with { Description = "AT01 SQL balance check" }, default));
        var submitted = await Call(s => s.Journal.TransitionJournal(cid, draft.ResourceId,
            clerk, Fixture.Key(), new(draft.Version), "submit", default));
        var approved = await Call(s => s.Journal.TransitionJournal(cid, draft.ResourceId,
            approver, Fixture.Key(), new(submitted.Version), "approve", default));
        var postKey = Fixture.Key();

        try
        {
            // Bypass the domain only to simulate corrupt hydrated financial history.
            // Line check constraints still require exactly one positive side.
            await using (var db = Context())
            {
                var changed = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [JournalLines] SET [Credit]=99, [FunctionalCredit]=99 WHERE [CompanyId]={cid} AND [JournalId]={draft.ResourceId} AND [Credit]>0");
                Ensure(changed == 1, "AT01 fixture must alter precisely one credit line");
            }

            int audits, receipts, outbox;
            long allocated;
            await using (var db = Context())
            {
                audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
                receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
                outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
                allocated = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber);
            }
            try
            {
                await Call(s => s.Journal.TransitionJournal(cid, draft.ResourceId, clerk,
                    postKey, new(approved.Version), "post", default));
                throw new Exception("AT01 approved unbalanced journal unexpectedly posted");
            }
            catch (BusinessException ex) when (ex.Code == "journal_unbalanced") { }

            await using (var db = Context())
            {
                var journal = await db.Set<JournalEntry>().SingleAsync(x =>
                    x.CompanyId == cid && x.Id == draft.ResourceId);
                Ensure(journal.State == JournalState.Approved && journal.Version == approved.Version &&
                    await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                    await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                    !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == postKey) &&
                    await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                    await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                        .SumAsync(x => x.LastNumber) == allocated,
                    "AT01 refused post modified approved journal, audit, receipt, outbox or numbering");
            }
        }
        finally
        {
            // Restore test data only; no application code or production record is changed.
            await using var db = Context();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [JournalLines] SET [Credit]=100, [FunctionalCredit]=100 WHERE [CompanyId]={cid} AND [JournalId]={draft.ResourceId} AND [Credit]=99");
        }

        // A failed attempt must not consume its idempotency key; retry after
        // correcting the accounting imbalance succeeds exactly once.
        var posted = await Call(s => s.Journal.TransitionJournal(cid, draft.ResourceId,
            clerk, postKey, new(approved.Version), "post", default));
        var replay = await Call(s => s.Journal.TransitionJournal(cid, draft.ResourceId,
            clerk, postKey, new(approved.Version), "post", default));
        Ensure(posted == replay, "AT01 corrected posting replay changed the receipt");
        await using (var db = Context())
        {
            Ensure(await db.Set<JournalEntry>().AnyAsync(x => x.CompanyId == cid &&
                x.Id == draft.ResourceId && x.State == JournalState.Posted) &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.ResourceId == draft.ResourceId) == 1,
                "AT01 correction did not post once with exactly one outbox event");
        }
        Console.WriteLine("  SQL PASS AT01 corrupted approved journal remains approved with zero effects; corrected retry posts once");
    }

    private static void Ensure(bool value, string reason)
    {
        if (!value) throw new Exception(reason);
    }
}
