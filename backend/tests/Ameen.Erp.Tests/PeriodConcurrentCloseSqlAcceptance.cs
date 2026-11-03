using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL11-T02 / BR09: independent SQL transactions racing to close the same
// period with distinct keys may produce one and only one close decision.
// This is general-ledger-period acceptance, not ERP-wide year-end approval.
public static class PeriodConcurrentCloseSqlAcceptance
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor approver)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> operation)
        {
            await using var db = Db();
            return await operation(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        // An isolated, unused year avoids interference with other SQL suites.
        var year = await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner,
            Fixture.Key(), new("GL11-RACE-2051",
                new DateOnly(2051, 1, 1), new DateOnly(2051, 12, 31)), default));
        FiscalPeriod period;
        await using (var db = Db())
            period = await db.Set<FiscalPeriod>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == cid && x.FiscalYearId == year.ResourceId &&
                x.StartsOn == new DateOnly(2051, 1, 1));

        async Task<(Guid Version, bool Closed, int PeriodCloseAudit, int AllAudit,
            int Receipts, int Outbox, int Journals, long Numbers)> Snapshot()
        {
            await using var db = Db();
            var saved = await db.Set<FiscalPeriod>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == cid && x.Id == period.Id);
            return (saved.Version, saved.IsClosed,
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.ResourceId == period.Id && x.Action == "period.closed"),
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber));
        }

        var before = await Snapshot();
        var ready = await Call(s => s.PeriodClose.PreviewPeriodClose(cid,
            period.Id, approver, 0, 10, default));
        Ensure(!before.Closed && ready.GeneralLedgerOnly &&
            ready.ExpectedVersion == before.Version &&
            ready.CanCloseGeneralLedger && ready.Checks.All(c => c.Passed),
            "GL11-T02 race fixture must start with an eligible open GL period");
        Ensure(before == await Snapshot(), "GL11-T02 close preview mutated SQL");

        var keys = Enumerable.Range(0, 4).Select(_ => Fixture.Key()).ToArray();
        var body = new ChangePeriodRequest(before.Version,
            "اختبار أربع محاولات إقفال متزامنة في نفس الفترة");
        async Task<(string Code, CommandReceipt? Receipt)> Compete(string key)
        {
            try
            {
                var receipt = await Call(s => s.PeriodClose.ChangePeriod(cid,
                    period.Id, approver, key, body, true, default));
                return ("closed", receipt);
            }
            catch (BusinessException ex) when (ex.Code is
                "version_conflict" or "period_state_conflict" or "write_conflict")
            {
                return (ex.Code, null);
            }
        }
        var outcomes = await Task.WhenAll(keys.Select(Compete));
        Ensure(outcomes.Count(x => x.Code == "closed") == 1 &&
            outcomes.Count(x => x.Receipt is null) == keys.Length - 1,
            "GL11-T02 simultaneous distinct close keys did not yield exactly one winner");
        var winner = Array.FindIndex(outcomes, x => x.Code == "closed");
        var successfulReceipt = outcomes[winner].Receipt!;
        var after = await Snapshot();
        Ensure(after.Closed && after.Version == successfulReceipt.Version &&
            after.Version != before.Version &&
            after.PeriodCloseAudit == before.PeriodCloseAudit + 1 &&
            after.AllAudit == before.AllAudit + 1 &&
            after.Receipts == before.Receipts + 1 &&
            after.Outbox == before.Outbox &&
            after.Journals == before.Journals &&
            after.Numbers == before.Numbers,
            "GL11-T02 concurrent close created duplicate decisions or financial side effects");
        await using (var db = Db())
        {
            var consumed = await db.Set<IdempotencyRecord>().AsNoTracking()
                .Where(x => x.CompanyId == cid && keys.Contains(x.Key))
                .Select(x => x.Key).ToArrayAsync();
            Ensure(consumed.Length == 1 && consumed[0] == keys[winner],
                "GL11-T02 losing concurrent close keys were consumed");
        }
        var replay = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, keys[winner], body, true, default));
        Ensure(replay == successfulReceipt && after == await Snapshot(),
            "GL11-T02 successful concurrent close replay changed SQL or receipt");
        Console.WriteLine("  SQL PASS GL11-T02 four distinct concurrent close keys yield one audited decision/receipt, losing keys untouched, replay stable");
    }

    private static void Ensure(bool passed, string reason)
    {
        if (!passed) throw new Exception(reason);
    }
}
