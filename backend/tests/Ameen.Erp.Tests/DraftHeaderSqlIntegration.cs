using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class DraftHeaderSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor clerk, Actor approver, CreateJournalRequest request)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        var draft = await Call(s => s.Journal.CreateGeneralOpening(cid, clerk, Fixture.Key(), GeneralOpeningTests.Request(request), default));
        DateOnly date;
        await using (var db = Context())
        {
            var source = await db.Set<JournalEntry>().SingleAsync(x => x.Id == draft.ResourceId);
            var period = await db.Set<FiscalPeriod>().SingleAsync(x => x.Id == source.FiscalPeriodId);
            var available = await db.Set<FiscalPeriod>().Where(x => x.CompanyId == cid && x.FiscalYearId == period.FiscalYearId &&
                x.Id != period.Id && !x.IsClosed).OrderByDescending(x => x.StartsOn).FirstAsync();
            date = available.StartsOn;
            var closed = await db.Set<FiscalPeriod>().FirstOrDefaultAsync(x => x.CompanyId == cid && x.IsClosed);
            if (closed is not null)
            {
                var denied = new EditGeneralOpeningRequest(draft.Version, "فترة مقفلة", "TB-CLOSED", request.Lines,
                    new(request.BranchId, closed.StartsOn, closed.StartsOn, null, closed.StartsOn));
                try { await Call(s => s.Journal.EditGeneralOpening(cid, draft.ResourceId, clerk, Fixture.Key(), denied, default)); throw new Exception("Closed target period was accepted"); }
                catch (BusinessException e) when (e.Code == "period_closed") { }
            }
        }
        var header = new DraftJournalHeaderRequest(request.BranchId, date.AddDays(1), date, "SQL-HEADER", date.AddDays(2));
        var edit = new EditGeneralOpeningRequest(draft.Version, "تصحيح الرأس والسطور", "TB-SQL-HEADER", request.Lines, header);
        var key = Fixture.Key();
        var failAudit = "ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_HeaderAudit] CHECK ([ResourceId] <> '" + draft.ResourceId.ToString("D") + "' OR [Action] <> 'general-opening.edited')";
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync(failAudit);
        try
        {
            await Call(s => s.Journal.EditGeneralOpening(cid, draft.ResourceId, clerk, key, edit, default));
            throw new Exception("Expected header audit failure");
        }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        finally
        {
            await using var db = Context(); await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_HeaderAudit]");
        }
        var unchanged = await Call(s => s.Journal.GetGeneralOpening(cid, draft.ResourceId, clerk, default));
        Ensure(unchanged.Version == draft.Version && unchanged.AccountingDate == request.AccountingDate && unchanged.OpeningRevision == 1 && unchanged.Revision == 1 && unchanged.LastModifiedAt == unchanged.CreatedAt, "Failed header edit left changes");
        var edits = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.Journal.EditGeneralOpening(cid, draft.ResourceId, clerk, key, edit, default))));
        Ensure(edits.Distinct().Count() == 1, "Concurrent header edit differs");
        var updated = await Call(s => s.Journal.GetGeneralOpening(cid, draft.ResourceId, clerk, default));
        Ensure(updated.AccountingDate == date && updated.DocumentDate == header.DocumentDate && updated.OperationStartsOn == header.OperationStartsOn && updated.ExternalReference == header.ExternalReference && updated.Number == draft.Number && updated.OpeningRevision == 2 && updated.Revision == 2 && updated.LastModifiedAt >= updated.CreatedAt, "SQL header replacement differs");
        await using (var db = Context())
        {
            var period = await db.Set<FiscalPeriod>().SingleAsync(x => x.Id == updated.FiscalPeriodId);
            Ensure(period.StartsOn <= date && period.EndsOn >= date, "Header period not derived from effect date");
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.ResourceId == draft.ResourceId && x.Action == "general-opening.edited") == 1, "Header replay duplicated audit");
        }
        var r = edits[0];
        r = await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, clerk, Fixture.Key(), new(r.Version), "submit", default));
        r = await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, approver, Fixture.Key(), new(r.Version), "approve", default));
        await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, approver, Fixture.Key(), new(r.Version), "post", default));
        await using (var db = Context())
        {
            var posted = await db.Set<JournalEntry>().SingleAsync(x => x.Id == draft.ResourceId);
            db.Entry(posted).Property(x => x.OperationStartsOn).CurrentValue = date.AddDays(3);
            try { await db.SaveChangesAsync(); throw new Exception("Posted header change was accepted"); }
            catch (BusinessException e) when (e.Code == "journal_header_immutable") { }
        }
        Console.WriteLine("  SQL PASS GL02 draft header period, concurrent replay, audit rollback, retry and posted header protection");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
