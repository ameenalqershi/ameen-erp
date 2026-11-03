using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL11-T02 bounded negative: a pending draft must block general-ledger
// period closure atomically and must not consume an idempotency decision.
// This does NOT claim all subsidiary reconciliations or annual profit close.
public static class PeriodPendingCloseSqlAcceptance
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk,
        Actor approver, CreateJournalRequest baseRequest)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Db();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        // Unique disposable year prevents dependence on other test suites'
        // shared periods, historical close/reopen audit and journal state.
        var year = await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner,
            Fixture.Key(), new("GL11-T02-SQL-2046",
                new DateOnly(2046, 1, 1), new DateOnly(2046, 12, 31)), default));
        FiscalPeriod period;
        await using (var db = Db())
            period = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid &&
                    x.FiscalYearId == year.ResourceId && x.StartsOn == new DateOnly(2046, 1, 1));

        var date = new DateOnly(2046, 1, 10);
        var draft = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(),
            baseRequest with
            {
                DocumentDate = date,
                AccountingDate = date,
                Description = "GL11-T02 period-close blocker",
                ExternalReference = "GL11-PENDING-" + Fixture.Key()
            }, default));
        var preview = await Call(s => s.PeriodClose.PreviewPeriodClose(
            cid, period.Id, approver, 0, 10, default));
        Ensure(!preview.CanCloseGeneralLedger && preview.GeneralLedgerOnly &&
            preview.UnpostedJournalCount == 1 &&
            preview.Issues.Items.Count == 1 &&
            preview.Issues.Items[0].JournalId == draft.ResourceId &&
            preview.Issues.Items[0].Code == "unposted_journal" &&
            !preview.Checks.Single(x => x.Code == "no_unposted_journals").Passed,
            "GL11-T02 preview missed an unposted draft from the selected period");

        async Task<(Guid PeriodVersion, bool Closed, int Audit, int Receipts, int Outbox,
            int Journals, long Numbers)> Snapshot()
        {
            await using var db = Db();
            var current = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            return (current.Version, current.IsClosed,
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber));
        }

        var prior = await Snapshot();
        var deniedKey = Fixture.Key();
        var closeBody = new ChangePeriodRequest(period.Version, "إقفال بعد تحقق");
        try
        {
            await Call(s => s.PeriodClose.ChangePeriod(
                cid, period.Id, approver, deniedKey, closeBody, true, default));
            throw new Exception("GL11-T02 closed a fiscal period with an unposted draft");
        }
        catch (BusinessException ex) when (ex.Code == "unposted_journals") { }

        Ensure(prior == await Snapshot(),
            "GL11-T02 rejected close modified period version/state, audit, receipts, outbox, journal or numbering");
        await using (var db = Db())
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(
                    x => x.CompanyId == cid && x.Key == deniedKey) &&
                !await db.Set<AuditEvent>().AnyAsync(x => x.CompanyId == cid &&
                    x.ResourceId == period.Id && x.Action == "period.closed"),
                "GL11-T02 denied close consumed a key or recorded a false close decision");

        // Repair the pending condition using the approved cancellation workflow,
        // then reuse that initially denied key: one actual close, one receipt.
        await Call(s => s.Journal.TransitionJournal(cid, draft.ResourceId, clerk,
            Fixture.Key(), new(draft.Version, "إلغاء مسودة مانعة"), "cancel", default));
        var ready = await Call(s => s.PeriodClose.PreviewPeriodClose(
            cid, period.Id, approver, 0, 10, default));
        Ensure(ready.CanCloseGeneralLedger && ready.UnpostedJournalCount == 0 &&
            ready.Issues.TotalCount == 0,
            "GL11-T02 cancelling a pending draft did not clear the preview blocker");
        var closed = await Call(s => s.PeriodClose.ChangePeriod(
            cid, period.Id, approver, deniedKey, closeBody, true, default));
        var replay = await Call(s => s.PeriodClose.ChangePeriod(
            cid, period.Id, approver, deniedKey, closeBody, true, default));
        Ensure(closed == replay && closed.Status == "Closed",
            "GL11-T02 retry after removing blocker did not return a stable close receipt");
        await using (var db = Db())
        {
            var saved = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            Ensure(saved.IsClosed && saved.Version == closed.Version &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.ResourceId == period.Id && x.Action == "period.closed") == 1 &&
                await db.Set<IdempotencyRecord>().CountAsync(x =>
                    x.CompanyId == cid && x.Key == deniedKey) == 1,
                "GL11-T02 recovery close did not persist exactly one audit and idempotency decision");
        }
        Console.WriteLine("  SQL PASS GL11-T02 pending journal close refused atomically; cancelled repair closes once with durable replay");
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
