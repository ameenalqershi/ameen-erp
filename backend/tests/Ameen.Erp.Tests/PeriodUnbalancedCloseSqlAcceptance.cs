using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL11-T02 and GL11-C01 bounded negative: no GL close can conceal an
// unbalanced, already-posted journal. SQL mutation is an isolated CI
// corruption fixture, never a production action or accounting policy.
public static class PeriodUnbalancedCloseSqlAcceptance
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk,
        Actor approver, CreateJournalRequest baseRequest)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> operation)
        {
            await using var db = Db();
            return await operation(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        var year = await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner,
            Fixture.Key(), new("GL11-BAL-2048",
                new DateOnly(2048, 1, 1), new DateOnly(2048, 12, 31)), default));
        FiscalPeriod period;
        await using (var db = Db())
            period = await db.Set<FiscalPeriod>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == cid && x.FiscalYearId == year.ResourceId &&
                x.StartsOn == new DateOnly(2048, 1, 1));
        var date = new DateOnly(2048, 1, 10);
        var request = baseRequest with
        {
            DocumentDate = date,
            AccountingDate = date,
            Description = "GL11-T02 posted balance guard",
            ExternalReference = "GL11-BAL-" + Fixture.Key()
        };
        Ensure(request.Lines.Count == 2 &&
            request.Lines[0].Debit == request.Lines[1].Credit &&
            request.Lines[1].Credit > 1 &&
            request.Lines[1].CurrencyCode == "SAR",
            "GL11-T02 corruption fixture expects balanced SAR journal");
        var created = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(), request, default));
        var submitted = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, clerk, Fixture.Key(), new(created.Version), "submit", default));
        var approved = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, approver, Fixture.Key(), new(submitted.Version), "approve", default));
        var posted = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, approver, Fixture.Key(), new(approved.Version), "post", default));
        Ensure(posted.Status == "Posted", "GL11-T02 corruption fixture needs posted journal");

        async Task<(Guid Version, bool Closed, int Audit, int Receipts, int Outbox,
            int Journals, long Numbers)> Snapshot()
        {
            await using var db = Db();
            var current = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            return (
                current.Version, current.IsClosed,
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber));
        }

        var originalCredit = request.Lines[1].Credit;
        var corruptedCredit = originalCredit - 1;
        var closeKey = Fixture.Key();
        var body = new ChangePeriodRequest(period.Version, "إقفال فترة مختبرة");
        try
        {
            // Exactly one already-posted credit line becomes imbalanced.
            // The SQL check constraints permit positive sides but the
            // finance aggregate must reject inconsistent historical amounts.
            await using (var db = Db())
            {
                var touched = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [JournalLines] SET [Credit]={corruptedCredit}, [FunctionalCredit]={corruptedCredit} WHERE [CompanyId]={cid} AND [JournalId]={posted.ResourceId} AND [Credit]>0");
                Ensure(touched == 1, "GL11-T02 fixture must corrupt precisely one posted credit line");
            }

            var before = await Snapshot();
            var preview = await Call(s => s.PeriodClose.PreviewPeriodClose(
                cid, period.Id, approver, 0, 10, default));
            Ensure(!preview.CanCloseGeneralLedger && preview.GeneralLedgerOnly &&
                preview.UnpostedJournalCount == 0 &&
                preview.PostedJournalCount == 1 &&
                preview.Issues.TotalCount == 1 &&
                preview.Issues.Items.Single().JournalId == posted.ResourceId &&
                preview.Issues.Items.Single().Code == "journal_unbalanced" &&
                !preview.Checks.Single(x => x.Code == "posted_journals_balanced").Passed,
                "GL11-T02 preview did not reveal corrupted posted journal balance");
            Ensure(before == await Snapshot(),
                "GL11-T02 preview of corrupt posting changed SQL effects");
            try
            {
                await Call(s => s.PeriodClose.ChangePeriod(cid,
                    period.Id, approver, closeKey, body, true, default));
                throw new Exception("GL11-T02 improperly closed an unbalanced posted journal");
            }
            catch (BusinessException ex) when (ex.Code == "journal_unbalanced") { }

            Ensure(before == await Snapshot(),
                "GL11-T02 denied close changed fiscal period, audit, receipts, outbox, journals or numbering");
            await using (var db = Db())
                Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(
                        x => x.CompanyId == cid && x.Key == closeKey) &&
                    !await db.Set<AuditEvent>().AnyAsync(x => x.CompanyId == cid &&
                        x.ResourceId == period.Id && x.Action == "period.closed"),
                    "GL11-T02 denied close wrote an idempotency or false audit decision");
        }
        finally
        {
            // Restore disposable test-only corruption even after a failed
            // assertion; no application or production record is edited.
            await using var db = Db();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [JournalLines] SET [Credit]={originalCredit}, [FunctionalCredit]={originalCredit} WHERE [CompanyId]={cid} AND [JournalId]={posted.ResourceId} AND [Credit]={corruptedCredit}");
        }

        var clear = await Call(s => s.PeriodClose.PreviewPeriodClose(
            cid, period.Id, approver, 0, 10, default));
        Ensure(clear.CanCloseGeneralLedger && clear.Issues.TotalCount == 0,
            "GL11-T02 repaired trial balance did not clear the close-preview blocker");
        var close = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, closeKey, body, true, default));
        var replay = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, closeKey, body, true, default));
        Ensure(close == replay && close.Status == "Closed",
            "GL11-T02 repaired balanced period did not close exactly once");
        await using (var db = Db())
        {
            var current = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            var saved = await db.Set<JournalEntry>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == posted.ResourceId);
            Ensure(current.IsClosed && current.Version == close.Version &&
                saved.State == JournalState.Posted && saved.Version == posted.Version &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.ResourceId == period.Id && x.Action == "period.closed") == 1 &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid &&
                    x.Key == closeKey) == 1,
                "GL11-T02 restored close altered financial history or persisted repeated decisions");
        }
        Console.WriteLine("  SQL PASS GL11-T02 unbalanced posted journal blocks close without effects; restored balance closes once and replays");
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
