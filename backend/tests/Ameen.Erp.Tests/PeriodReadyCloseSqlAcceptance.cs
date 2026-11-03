using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL11-T01 bounded positive: an eligible general-ledger period closes once
// after all posted/cancelled journal decisions are settled. This does not
// represent ERP-wide year close or make any retained-earnings accounting entry.
public static class PeriodReadyCloseSqlAcceptance
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

        // Unique year avoids interference from any SQL test's journals,
        // inventory openings and prior close/reopen audit decisions.
        var year = await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner,
            Fixture.Key(), new("GL11-T01-2047",
                new DateOnly(2047, 1, 1), new DateOnly(2047, 12, 31)), default));
        FiscalPeriod period;
        await using (var db = Db())
            period = await db.Set<FiscalPeriod>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == cid && x.FiscalYearId == year.ResourceId &&
                x.StartsOn == new DateOnly(2047, 1, 1));

        var date = new DateOnly(2047, 1, 10);
        CreateJournalRequest Request(string label) => baseRequest with
        {
            DocumentDate = date, AccountingDate = date,
            Description = label, ExternalReference = label + "-" + Fixture.Key()
        };
        var draft = await Call(s => s.Journal.CreateJournal(cid, clerk,
            Fixture.Key(), Request("GL11-T01-POSTED"), default));
        var submitted = await Call(s => s.Journal.TransitionJournal(cid,
            draft.ResourceId, clerk, Fixture.Key(), new(draft.Version), "submit", default));
        var approved = await Call(s => s.Journal.TransitionJournal(cid,
            draft.ResourceId, approver, Fixture.Key(), new(submitted.Version), "approve", default));
        var posted = await Call(s => s.Journal.TransitionJournal(cid,
            draft.ResourceId, approver, Fixture.Key(), new(approved.Version), "post", default));
        var cancelDraft = await Call(s => s.Journal.CreateJournal(cid, clerk,
            Fixture.Key(), Request("GL11-T01-CANCELLED"), default));
        var cancelled = await Call(s => s.Journal.TransitionJournal(cid,
            cancelDraft.ResourceId, clerk, Fixture.Key(),
            new(cancelDraft.Version, "إلغاء قبل الإقفال"), "cancel", default));
        Ensure(posted.Status == "Posted" && cancelled.Status == "Cancelled",
            "GL11-T01 fixture must contain one posted and one cancelled journal");

        async Task<(int Audit, int Receipt, int Outbox, int Journals, long Numbers)> Snapshot()
        {
            await using var db = Db();
            return (
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber));
        }

        // GL11-A01/T02: this company-wide operation is not a branch-scoped
        // closing privilege. Reject a non-member and a member granted
        // PeriodClose on only one branch, without reserving any receipt.
        var limitedCloser = new Actor("gl11-sql-branch-only-closer");
        await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(),
            new(limitedCloser.Subject, new[] { Permissions.Read,
                Permissions.PeriodClose }, false, new[] { baseRequest.BranchId },
                null), default));
        var outsider = new Actor("gl11-sql-nonmember-closer");
        var limitedKey = Fixture.Key();
        var outsiderKey = Fixture.Key();
        var deniedBody = new ChangePeriodRequest(period.Version,
            "محاولة إقفال غير مصرح بها");
        var denialCounts = await Snapshot();
        try
        {
            await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
                limitedCloser, limitedKey, deniedBody, true, default));
            throw new Exception("GL11-T02 branch-only closer closed the company period");
        }
        catch (BusinessException ex) when (ex.Code == "all_branches_required") { }
        try
        {
            await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
                outsider, outsiderKey, deniedBody, true, default));
            throw new Exception("GL11-T02 nonmember closed the company period");
        }
        catch (BusinessException ex) when (ex.Code == "permission_denied") { }
        Ensure(denialCounts == await Snapshot(),
            "GL11-T02 unauthorized period close changed SQL state");
        await using (var deniedDb = Db())
        {
            var unchanged = await deniedDb.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            Ensure(!unchanged.IsClosed && unchanged.Version == period.Version &&
                !await deniedDb.Set<IdempotencyRecord>().AnyAsync(x =>
                    x.CompanyId == cid && (x.Key == limitedKey ||
                        x.Key == outsiderKey)),
                "GL11-T02 denied closer altered period or consumed receipt");
        }

        var previewCounts = await Snapshot();
        var preview = await Call(s => s.PeriodClose.PreviewPeriodClose(
            cid, period.Id, approver, 0, 10, default));
        Ensure(preview.GeneralLedgerOnly && preview.CanCloseGeneralLedger &&
            preview.ExpectedVersion == period.Version &&
            preview.PostedJournalCount == 1 && preview.UnpostedJournalCount == 0 &&
            preview.Issues.TotalCount == 0 && preview.Checks.All(x => x.Passed),
            "GL11-T01 eligible posted/cancelled journal period was not previewable");
        Ensure(previewCounts == await Snapshot(),
            "GL11-T01 preview mutated journals, audit, receipts, outbox or numbering");

        var closeKey = Fixture.Key();
        var body = new ChangePeriodRequest(period.Version, "إقفال فترة دفتر الأستاذ التجريبي");
        var closed = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, closeKey, body, true, default));
        var replay = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, closeKey, body, true, default));
        Ensure(closed == replay && closed.Status == "Closed",
            "GL11-T01 exact close replay changed result");

        var afterClose = await Call(s => s.PeriodClose.PreviewPeriodClose(
            cid, period.Id, approver, 0, 10, default));
        var history = await Call(s => s.PeriodClose.PeriodExecutionHistory(
            cid, period.Id, approver, 0, 10, default));
        Ensure(afterClose.GeneralLedgerOnly && !afterClose.CanCloseGeneralLedger &&
            afterClose.Checks.Single(x => x.Code == "period_open").Passed == false &&
            history.History.TotalCount == 1 &&
            history.History.Items.Single().Action == "period.closed",
            "GL11-T01 closed period did not expose exactly one audited decision");
        var savedCounts = await Snapshot();

        // BR09/GL11-T02: a successful close cannot be replayed with a changed
        // payload, nor can another key close the already-closed period. A
        // rejected command must never reserve a receipt or mutate the ledger.
        var alteredBody = body with { Reason = "محاولة تعديل سبب الإقفال بنفس المفتاح" };
        try
        {
            await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
                approver, closeKey, alteredBody, true, default));
            throw new Exception("GL11-T02 conflicting replay accepted altered reason");
        }
        catch (BusinessException ex) when (ex.Code == "idempotency_conflict") { }

        var duplicateKey = Fixture.Key();
        try
        {
            await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
                approver, duplicateKey, new ChangePeriodRequest(closed.Version,
                    "إقفال ثانٍ مرفوض"), true, default));
            throw new Exception("GL11-T02 duplicate close accepted a fresh key");
        }
        catch (BusinessException ex) when (ex.Code == "period_state_conflict") { }

        Ensure(savedCounts == await Snapshot(),
            "GL11-T02 conflicting replay or second close changed SQL state");
        await using (var rejectedDb = Db())
        {
            Ensure(!await rejectedDb.Set<IdempotencyRecord>().AnyAsync(x =>
                x.CompanyId == cid && x.Key == duplicateKey) &&
                await rejectedDb.Set<AuditEvent>().CountAsync(x =>
                    x.CompanyId == cid && x.ResourceId == period.Id &&
                    x.Action == "period.closed") == 1,
                "GL11-T02 rejected duplicate close consumed receipt or audit");
        }

        Ensure(savedCounts.Audit == previewCounts.Audit + 1 &&
            savedCounts.Receipt == previewCounts.Receipt + 1 &&
            savedCounts.Outbox == previewCounts.Outbox &&
            savedCounts.Journals == previewCounts.Journals &&
            savedCounts.Numbers == previewCounts.Numbers,
            "GL11-T01 close changed financial ledger, outbox, numbering or duplicated decisions");

        await using (var db = Db())
        {
            var current = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            var state = await db.Set<JournalEntry>().AsNoTracking()
                .Where(x => x.CompanyId == cid &&
                    (x.Id == posted.ResourceId || x.Id == cancelled.ResourceId))
                .Select(x => new { x.Id, x.State, x.Version }).ToArrayAsync();
            Ensure(current.IsClosed && current.Version == closed.Version &&
                state.Length == 2 &&
                state.Single(x => x.Id == posted.ResourceId).State == JournalState.Posted &&
                state.Single(x => x.Id == posted.ResourceId).Version == posted.Version &&
                state.Single(x => x.Id == cancelled.ResourceId).State == JournalState.Cancelled &&
                state.Single(x => x.Id == cancelled.ResourceId).Version == cancelled.Version &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.ResourceId == period.Id && x.Action == "period.closed") == 1 &&
                await db.Set<IdempotencyRecord>().CountAsync(x =>
                    x.CompanyId == cid && x.Key == closeKey) == 1,
                "GL11-T01 positive period close altered posted/cancelled journals or duplicated SQL audit");
        }
        Console.WriteLine("  SQL PASS GL11-T01 eligible period preview read-only, posted+cancelled preserved, one close/replay, denies unauthorized branch-only/nonmember closes, rejects conflicting payload/second key atomically, no financial side effects");
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
