using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL11-A05: disposable SQL-backed history of period-close decisions.
public static class PeriodExecutionHistorySqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor approver)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Context();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        FiscalPeriod period;
        await using (var db = Context())
            period = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.StartsOn == new DateOnly(2026, 12, 1));
        Ensure(!period.IsClosed, "Test December period unexpectedly closed");

        var closed = await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
            approver, Fixture.Key(), new(period.Version, "إقفال SQL خاص بالسجل"),
            true, default));
        var reopened = await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
            approver, Fixture.Key(), new(closed.Version, "فتح SQL خاص بالسجل"),
            false, default));
        int audits, receipts, outbox, journals;
        long numbers;
        await using (var db = Context())
        {
            audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            numbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                .SumAsync(x => x.LastNumber);
        }
        var whole = await Call(s => s.PeriodClose.PeriodExecutionHistory(cid,
            period.Id, owner, 0, 10, default));
        var page1 = await Call(s => s.PeriodClose.PeriodExecutionHistory(cid,
            period.Id, owner, 0, 1, default));
        var page2 = await Call(s => s.PeriodClose.PeriodExecutionHistory(cid,
            period.Id, owner, 1, 1, default));
        var page3 = await Call(s => s.PeriodClose.PeriodExecutionHistory(cid,
            period.Id, owner, 2, 1, default));
        Ensure(!whole.IsClosed && whole.Version == reopened.Version &&
            whole.History.TotalCount == 2 && whole.History.Items.Count == 2 &&
            whole.History.Items.Any(x => x.Action == "period.closed" &&
                x.ActorId == approver.Subject && System.Text.Json.JsonDocument.Parse(x.Details).RootElement.GetProperty("reason").GetString() == "إقفال SQL خاص بالسجل") &&
            whole.History.Items.Any(x => x.Action == "period.reopened" &&
                x.ActorId == approver.Subject && System.Text.Json.JsonDocument.Parse(x.Details).RootElement.GetProperty("reason").GetString() == "فتح SQL خاص بالسجل") &&
            page1.History.Items.Count == 1 && page2.History.Items.Count == 1 &&
            page1.History.Items[0].Id != page2.History.Items[0].Id &&
            page3.History.TotalCount == 2 && page3.History.Items.Count == 0,
            "GL11 SQL close/reopen execution history missing or incorrectly paginated");
        await Expect("permission_denied", () => Call(s => s.PeriodClose.PeriodExecutionHistory(
            cid, period.Id, new Actor("gl11-no-membership"), 0, 10, default)));
        await using (var db = Context())
        {
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).SumAsync(x => x.LastNumber) == numbers &&
                !await db.Set<FiscalPeriod>().AnyAsync(x => x.CompanyId == cid &&
                    x.Id == period.Id && x.IsClosed),
                "GL11 SQL history read mutated accounting state");
        }
        Console.WriteLine("  SQL PASS GL11-A05 close/reopen execution result, audit paging, tenant denial and read-only totals");
    }
    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    private static void Ensure(bool valid, string error)
    {
        if (!valid) throw new Exception(error);
    }
}
