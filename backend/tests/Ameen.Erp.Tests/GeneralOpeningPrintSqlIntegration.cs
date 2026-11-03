using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL02-A07: isolated SQL-backed read-only printable snapshot and access tests.
public static class GeneralOpeningPrintSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor clerk, Actor approver,
        CreateJournalRequest baseRequest)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        const string description = "افتتاح SQL مستقل";
        const string reference = "00000040-EXT";
        var request = GeneralOpeningTests.Request(baseRequest) with
        {
            Description = description, ExternalReference = reference,
            PreviousTrialBalanceReference = "TB-SQL-PRINT"
        };
        var created = await Call(s => s.Journal.CreateGeneralOpening(cid, clerk,
            Fixture.Key(), request, default));
        int audits, receipts, journals, outbox;
        long numbers;
        await using (var db = Context())
        {
            audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            numbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                .SumAsync(x => x.LastNumber);
        }

        var printed = await Call(s => s.Journal.GeneralOpeningPrint(cid, created.ResourceId, clerk, default));
        var repeated = await Call(s => s.Journal.GeneralOpeningPrint(cid, created.ResourceId, clerk, default));
        Ensure(printed.JournalId == created.ResourceId &&
            printed.Number == created.Number && printed.State == nameof(JournalState.Draft) &&
            printed.Description == description && printed.ExternalReference == reference &&
            printed.PreviousTrialBalanceReference == "TB-SQL-PRINT" &&
            printed.OpeningCurrency == "SAR" && printed.OpeningExchangeRate == 1m &&
            printed.FunctionalCurrency == "SAR" && printed.Lines.Count == request.Lines.Length &&
            printed.Lines.Any(x => x.AccountCode == "1" && x.AccountName == "نقدية") &&
            printed.Lines.Any(x => x.AccountCode == "3" && x.AccountName == "رأس المال") &&
            printed.FunctionalDebit == 100 && printed.FunctionalCredit == 100 &&
            repeated.Revision == printed.Revision && repeated.TotalDebit == printed.TotalDebit,
            "GL02 SQL printable snapshot omitted persisted header, totals or account metadata");

        try
        {
            await Call(s => s.Journal.GeneralOpeningPrint(cid, created.ResourceId,
                new Actor("gl02-print-nonmember"), default));
            throw new Exception("GL02 SQL print disclosed to unauthorized tenant member");
        }
        catch (BusinessException e) when (e.Code == "permission_denied") { }

        await using (var db = Context())
        {
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber) == numbers,
                "GL02 SQL print affected journal, audit, receipt, outbox or numbering");
        }

        var submitted = await Call(s => s.Journal.TransitionJournal(cid, created.ResourceId,
            clerk, Fixture.Key(), new(created.Version), "submit", default));
        var approved = await Call(s => s.Journal.TransitionJournal(cid, created.ResourceId,
            approver, Fixture.Key(), new(submitted.Version), "approve", default));
        await Call(s => s.Journal.TransitionJournal(cid, created.ResourceId,
            clerk, Fixture.Key(), new(approved.Version), "post", default));
        var posted = await Call(s => s.Journal.GeneralOpeningPrint(cid, created.ResourceId, clerk, default));
        Ensure(posted.State == nameof(JournalState.Posted) &&
            posted.TotalDebit == printed.TotalDebit && posted.TotalCredit == printed.TotalCredit &&
            posted.FunctionalDebit == printed.FunctionalDebit &&
            posted.FunctionalCredit == printed.FunctionalCredit &&
            posted.Revision > printed.Revision,
            "GL02 print repriced or changed the persisted posting history");
        Console.WriteLine("  SQL PASS GL02-A07 printable historical opening, exact totals, tenant permissions and no read side effects");
    }

    private static void Ensure(bool value, string error)
    {
        if (!value) throw new Exception(error);
    }
}
