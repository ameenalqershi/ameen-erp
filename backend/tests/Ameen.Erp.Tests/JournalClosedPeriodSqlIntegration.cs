using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// AT02 must hold at the actual SQL transaction boundary, not just in memory.
// The suite operates only on the disposable database created by SqlIntegration.
public static class JournalClosedPeriodSqlIntegration
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
        Guid closedPeriodId;
        string closedCode;
        bool wasClosed;
        DateOnly openDate;
        await using (var db = Context())
        {
            var period = await db.Set<FiscalPeriod>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == cid && x.StartsOn <= example.AccountingDate &&
                x.EndsOn >= example.AccountingDate);
            var open = await db.Set<FiscalPeriod>().AsNoTracking()
                .Where(x => x.CompanyId == cid && x.StartsOn > period.EndsOn && !x.IsClosed)
                .OrderBy(x => x.StartsOn).FirstAsync();
            closedPeriodId = period.Id;
            closedCode = period.Code;
            wasClosed = period.IsClosed;
            openDate = open.StartsOn.AddDays(1);
        }

        // A historical document date is allowed when the effective date is open.
        var valid = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(),
            example with { DocumentDate = example.AccountingDate, AccountingDate = openDate }, default));
        try
        {
            await using (var db = Context())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [FiscalPeriods] SET [IsClosed]=CAST(1 AS bit) WHERE [Id]={closedPeriodId} AND [CompanyId]={cid}");

            int journals, audits, receipts, outbox;
            long allocated;
            await using (var db = Context())
            {
                journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
                audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
                receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
                outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
                allocated = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber);
            }
            async Task ExpectClosed(Func<Task> work)
            {
                try
                {
                    await work();
                    throw new Exception("AT02 unexpectedly allowed a closed accounting period");
                }
                catch (BusinessException e) when (e.Code == "period_closed" &&
                    e.Message.Contains(closedCode, StringComparison.Ordinal)) { }
            }
            await ExpectClosed(() => Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(),
                example with { DocumentDate = openDate }, default)));

            var bypassHeader = new DraftJournalHeaderRequest(example.BranchId, openDate,
                example.AccountingDate, "AT02-CLOSED", null);
            await ExpectClosed(() => Call(s => s.Journal.EditJournal(cid, valid.ResourceId, clerk,
                Fixture.Key(), new(valid.Version, "محاولة تغيير تاريخ الوثيقة", example.Lines,
                    bypassHeader), default)));

            await using (var db = Context())
            {
                var saved = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == cid &&
                    x.Id == valid.ResourceId);
                Ensure(saved.Version == valid.Version && saved.State == JournalState.Draft &&
                    saved.AccountingDate == openDate && saved.DocumentDate == example.AccountingDate &&
                    await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals &&
                    await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                    await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                    await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                    await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                        .SumAsync(x => x.LastNumber) == allocated,
                    "Rejected AT02 create/edit changed journal, audit, receipt, outbox or numbering in SQL");
            }

            var submitted = await Call(s => s.Journal.TransitionJournal(cid, valid.ResourceId,
                clerk, Fixture.Key(), new(valid.Version), "submit", default));
            var approved = await Call(s => s.Journal.TransitionJournal(cid, valid.ResourceId,
                approver, Fixture.Key(), new(submitted.Version), "approve", default));
            await Call(s => s.Journal.TransitionJournal(cid, valid.ResourceId, clerk,
                Fixture.Key(), new(approved.Version), "post", default));
            await using (var db = Context())
                Ensure(await db.Set<JournalEntry>().AnyAsync(x => x.CompanyId == cid &&
                    x.Id == valid.ResourceId && x.State == JournalState.Posted &&
                    x.DocumentDate == example.AccountingDate && x.AccountingDate == openDate),
                    "Valid open accounting date was not posted independently of historical document date");
            Console.WriteLine("  SQL PASS AT02 closed effect period rejects open document-date bypass and stale header without side effects");
        }
        finally
        {
            // Restore the disposable fixture's previous status for subsequent SQL suites.
            await using var db = Context();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [FiscalPeriods] SET [IsClosed]={wasClosed} WHERE [Id]={closedPeriodId} AND [CompanyId]={cid}");
        }
    }

    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
