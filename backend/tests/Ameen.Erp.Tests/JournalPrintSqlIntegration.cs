using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL03-A07 and GL07-A07 printable journal acceptance on a disposable SQL database.
public static class JournalPrintSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor clerk, Actor approver,
        CreateJournalRequest request)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        var draft = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(),
            request with
            {
                Description = "قيد قابل للطباعة", ExternalReference = "000001-GLOBAL"
            }, default));
        var source = await Call(s => s.Journal.JournalPrint(cid, draft.ResourceId, clerk, default));
        Ensure(source.Number == draft.Number && source.State == nameof(JournalState.Draft) &&
            source.Source == nameof(JournalSource.Manual) &&
            source.OriginalTotalsCurrency == "SAR" &&
            source.TotalDebit == 100m && source.TotalCredit == 100m &&
            source.FunctionalDebit == 100m && source.FunctionalCredit == 100m &&
            source.Lines.Any(x => x.AccountCode == "1" && x.AccountName == "نقدية") &&
            source.Lines.Any(x => x.AccountCode == "3" && x.AccountName == "رأس المال"),
            "SQL GL03 print omitted persisted numbers, account identities or balances");

        await Expect("permission_denied", () => Call(s => s.Journal.JournalPrint(
            cid, draft.ResourceId, new Actor("journal-print-outsider"), default)));
        var submitted = await Call(s => s.Journal.TransitionJournal(cid,
            draft.ResourceId, clerk, Fixture.Key(), new(draft.Version), "submit", default));
        var approved = await Call(s => s.Journal.TransitionJournal(cid,
            draft.ResourceId, approver, Fixture.Key(), new(submitted.Version), "approve", default));
        var posted = await Call(s => s.Journal.TransitionJournal(cid,
            draft.ResourceId, clerk, Fixture.Key(), new(approved.Version), "post", default));
        var inverse = await Call(s => s.Journal.ReverseJournal(cid, draft.ResourceId, clerk,
            Fixture.Key(), new(posted.Version, Fixture.Date, "عكس SQL للحركة"), default));

        int journals, audit, receipts, outbox;
        long numbers;
        await using (var db = Context())
        {
            journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            audit = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            numbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                .SumAsync(x => x.LastNumber);
        }

        var reversal = await Call(s => s.Journal.JournalPrint(cid, inverse.ResourceId, clerk, default));
        var saved = await Call(s => s.Journal.JournalPrint(cid, posted.ResourceId, clerk, default));
        Ensure(saved.State == nameof(JournalState.Posted) &&
            saved.PostedAt is not null &&
            reversal.State == nameof(JournalState.Draft) &&
            reversal.Source == nameof(JournalSource.Reversal) &&
            reversal.OriginalJournalNumber == saved.Number &&
            reversal.ReversalReason == "عكس SQL للحركة" &&
            reversal.Lines.Count == saved.Lines.Count &&
            reversal.Lines.Zip(saved.Lines).All(pair =>
                pair.First.AccountCode == pair.Second.AccountCode &&
                pair.First.CurrencyCode == pair.Second.CurrencyCode &&
                pair.First.ExchangeRate == pair.Second.ExchangeRate &&
                pair.First.Debit == pair.Second.Credit &&
                pair.First.Credit == pair.Second.Debit &&
                pair.First.FunctionalDebit == pair.Second.FunctionalCredit &&
                pair.First.FunctionalCredit == pair.Second.FunctionalDebit) &&
            reversal.FunctionalDebit == saved.FunctionalCredit &&
            reversal.FunctionalCredit == saved.FunctionalDebit,
            "SQL GL07 reversal print lost original link or historical opposite rates");

        await using (var db = Context())
        {
            Ensure(await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audit &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber) == numbers,
                "SQL GL03/GL07 printing mutated journal, audit, idempotency, outbox or numbering");
        }
        Console.WriteLine("  SQL PASS GL03-A07/GL07-A07 manual and reversal printable history, denied outsider and zero side effects");
    }

    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected GL03/GL07 error " + code);
    }
    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
