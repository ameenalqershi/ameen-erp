using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class JournalProcessingSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk, Actor approver, CreateJournalRequest request)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        async Task<CommandReceipt> Draft() => await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(), request, default));
        async Task<CommandReceipt> Approve(CommandReceipt r)
        {
            r = await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, clerk, Fixture.Key(), new(r.Version), "submit", default));
            return await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, approver, Fixture.Key(), new(r.Version), "approve", default));
        }
        var first = await Approve(await Draft()); var rejected = await Draft(); var last = await Approve(await Draft());
        var items = new[] { new JournalProcessingItem(first.ResourceId, first.Version, Fixture.Key()), new JournalProcessingItem(rejected.ResourceId, rejected.Version, Fixture.Key()), new JournalProcessingItem(last.ResourceId, last.Version, Fixture.Key()) };
        var command = new ProcessJournalsRequest("post", items);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.Journal.ProcessJournals(cid, approver, command, default))));
        Ensure(results.All(x => x.SucceededCount == 2 && x.FailedCount == 1 && !x.AllSucceeded && x.Rows[1].RejectionRecorded), "Mixed concurrent batch results differ");
        Ensure(results.All(x => x.Rows.SequenceEqual(results[0].Rows)), "Concurrent batch replay differs");
        await using (var db = Context())
        {
            var ids = items.Select(x => x.JournalId).ToArray();
            Ensure(await db.Set<OutboxEvent>().CountAsync(x => ids.Contains(x.ResourceId)) == 2, "Batch produced duplicate effects");
            Ensure(await db.Set<AuditEvent>().CountAsync(x => ids.Contains(x.ResourceId) && x.Action.StartsWith("journal.processing-")) == 3, "Batch history duplicate/missing");
        }
        var fail = await Approve(await Draft()); var succeed = await Approve(await Draft());
        var retryItems = new[] { new JournalProcessingItem(fail.ResourceId, fail.Version, Fixture.Key()), new JournalProcessingItem(succeed.ResourceId, succeed.Version, Fixture.Key()) };
        // DDL cannot parameterize a CHECK literal; the only inserted value is a generated, typed Guid.
        var failureConstraint = "ALTER TABLE [OutboxEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_ProcessingOutbox] CHECK ([ResourceId] <> '" + fail.ResourceId.ToString("D") + "')";
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync(failureConstraint);
        var partial = await Call(s => s.Journal.ProcessJournals(cid, approver, new("post", retryItems), default));
        Ensure(partial.Rows[0].ErrorCode == "database_constraint" && !partial.Rows[0].RejectionRecorded && partial.Rows[1].Succeeded, "Failed document prevented independent success");
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] DROP CONSTRAINT [CK_Test_ProcessingOutbox]");
            Ensure(!await db.Set<AuditEvent>().AnyAsync(x => x.ResourceId == fail.ResourceId && x.Action.StartsWith("journal.processing-")), "Failed transaction left history");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == retryItems[0].IdempotencyKey), "Failed transaction consumed key");
        }
        var retried = await Call(s => s.Journal.ProcessJournals(cid, approver, new("post", retryItems), default));
        Ensure(retried.AllSucceeded && retried.Rows[1].Receipt == partial.Rows[1].Receipt, "Retry duplicated successful peer");
        // Simulate stale tracked membership/period reads in a reused scoped service, then revoke externally.
        await using (var db = Context())
        {
            var services = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
            var member = await db.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == approver.Subject);
            var originalPermissions = member.GrantedPermissions.ToArray();
            await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(), new(approver.Subject,
                originalPermissions.Where(x => x != Permissions.Post).ToArray(), member.AllBranches, member.BranchIds.ToArray(), member.Version), default));
            try
            {
                var stale = await services.Journal.ProcessJournals(cid, approver, command, default);
                Ensure(stale.Rows.All(x => !x.Succeeded && x.ErrorCode == "permission_denied" && x.Receipt == null), "Tracked membership bypassed fresh replay authorization");
            }
            finally
            {
                await using var fresh = Context(); var current = await fresh.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == approver.Subject);
                await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(), new(approver.Subject, originalPermissions, current.AllBranches, current.BranchIds.ToArray(), current.Version), default));
            }
        }
        Console.WriteLine("  SQL PASS GL05/06 mixed concurrent batch replay, durable rejection, per-document rollback/retry, history and refreshed authorization");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
