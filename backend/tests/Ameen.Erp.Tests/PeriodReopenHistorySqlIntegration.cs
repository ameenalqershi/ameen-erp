using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL12-A05 acceptance: a disposable SQL database, real audit events and a
// reader with PeriodReopen but *without* PeriodClose permission.
public static class PeriodReopenHistorySqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor operatorActor)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> fn)
        {
            await using var db = Db();
            return await fn(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        FiscalPeriod period;
        await using (var db = Db())
        {
            var busyJournals = await db.Set<JournalEntry>()
                .Where(x => x.CompanyId == cid).Select(x => x.FiscalPeriodId)
                .Distinct().ToArrayAsync();
            var busyOpenings = await db.Set<InventoryOpening>()
                .Where(x => x.CompanyId == cid).Select(x => x.FiscalPeriodId)
                .Distinct().ToArrayAsync();
            var priorDecisions = await db.Set<AuditEvent>()
                .Where(x => x.CompanyId == cid && (x.Action == "period.closed" ||
                    x.Action == "period.reopened")).Select(x => x.ResourceId)
                .Distinct().ToArrayAsync();
            period = await db.Set<FiscalPeriod>().AsNoTracking()
                .Where(x => x.CompanyId == cid && !x.IsClosed &&
                    !busyJournals.Contains(x.Id) && !busyOpenings.Contains(x.Id) &&
                    !priorDecisions.Contains(x.Id)).OrderBy(x => x.StartsOn).FirstAsync();
        }

        var reviewer = new Actor("gl12-sql-reopen-reviewer");
        var closer = new Actor("gl12-sql-close-only-reviewer");
        await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(),
            new(reviewer.Subject,
                new[] { Permissions.Read, Permissions.Audit, Permissions.PeriodReopen },
                true, Array.Empty<Guid>(), null), default));
        await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(),
            new(closer.Subject,
                new[] { Permissions.Read, Permissions.Audit, Permissions.PeriodClose },
                true, Array.Empty<Guid>(), null), default));
        var initial = await Call(s => s.PeriodClose.ReopenExecutionHistory(cid,
            period.Id, reviewer, 0, 10, default));
        Ensure(!initial.IsClosed && initial.History.TotalCount == 0,
            "Fresh SQL period unexpectedly contains reopen records");

        var closed = await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
            operatorActor, Fixture.Key(), new(period.Version, "GL12 SQL إغلاق أول"),
            true, default));
        var reopened = await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
            operatorActor, Fixture.Key(), new(closed.Version, "GL12 SQL إعادة فتح أولى"),
            false, default));
        var closedAgain = await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
            operatorActor, Fixture.Key(), new(reopened.Version, "GL12 SQL إغلاق ثان"),
            true, default));
        var reopenedAgain = await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
            operatorActor, Fixture.Key(), new(closedAgain.Version, "GL12 SQL إعادة فتح ثانية"),
            false, default));

        int journals, audits, receipts, outbox;
        long numbers;
        await using (var db = Db())
        {
            journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            numbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                .SumAsync(x => x.LastNumber);
        }

        var whole = await Call(s => s.PeriodClose.ReopenExecutionHistory(cid,
            period.Id, reviewer, 0, 10, default));
        var p1 = await Call(s => s.PeriodClose.ReopenExecutionHistory(cid,
            period.Id, reviewer, 0, 1, default));
        var p2 = await Call(s => s.PeriodClose.ReopenExecutionHistory(cid,
            period.Id, reviewer, 1, 1, default));
        var p3 = await Call(s => s.PeriodClose.ReopenExecutionHistory(cid,
            period.Id, reviewer, 2, 1, default));
        var reasons = whole.History.Items.Select(x =>
            System.Text.Json.JsonDocument.Parse(x.Details)
                .RootElement.GetProperty("reason").GetString()).OrderBy(x => x).ToArray();
        Ensure(!whole.IsClosed && whole.Version == reopenedAgain.Version &&
            whole.History.TotalCount == 2 && whole.History.Items.Count == 2 &&
            whole.History.Items.All(x => x.Action == "period.reopened" &&
                x.ActorId == operatorActor.Subject) &&
            reasons.SequenceEqual(new[] { "GL12 SQL إعادة فتح أولى",
                "GL12 SQL إعادة فتح ثانية" }.OrderBy(x => x)) &&
            p1.History.TotalCount == 2 && p2.History.TotalCount == 2 &&
            p1.History.Items.Count == 1 && p2.History.Items.Count == 1 &&
            p1.History.Items[0].Id != p2.History.Items[0].Id &&
            p3.History.TotalCount == 2 && p3.History.Items.Count == 0,
            "GL12 SQL history mixed close events or lost reopened decisions");

        await Expect("permission_denied", () => Call(s =>
            s.PeriodClose.ReopenExecutionHistory(cid, period.Id, closer, 0, 10, default)));
        await Expect("permission_denied", () => Call(s =>
            s.PeriodClose.ReopenExecutionHistory(cid, period.Id,
                new Actor("gl12-outsider"), 0, 10, default)));

        await using (var db = Db())
        {
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber) == numbers &&
                !await db.Set<FiscalPeriod>().AnyAsync(x => x.CompanyId == cid &&
                    x.Id == period.Id && x.IsClosed),
                "GL12 SQL reopening history read mutated financial state");
        }
        Console.WriteLine("  SQL PASS GL12-A05 reopening-only results/history, paging, independent PeriodReopen rights and no read side effects");
    }
    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected GL12 SQL error " + code);
    }
    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
