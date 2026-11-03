using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL03-T02: an ordinary manual journal line has exactly one positive side.
// Control accounts belong to explicitly authorized subsidiary-source posting.
public static class JournalLineGuardAcceptance
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-T02: both-positive, zero and negative journal lines reject without side effects", async f =>
        {
            var initialAudit = f.Store.Count<AuditEvent>();
            var initialReceipts = f.Store.Count<IdempotencyRecord>();
            var initialJournals = f.Store.Count<JournalEntry>();
            var initialNumbers = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            var cases = new (decimal Debit, decimal Credit, string Code)[]
            {
                (50m, 50m, "invalid_journal_side"),
                (0m, 0m, "invalid_journal_side"),
                (-1m, 0m, "invalid_amount"),
                (0m, -1m, "invalid_amount")
            };
            foreach (var (debit, credit, code) in cases)
            {
                var request = f.Request();
                request.Lines[0] = request.Lines[0] with { Debit = debit, Credit = credit };
                var key = Fixture.Key();
                await Reject(code, () => f.Draft(request, key));
                Ensure(!f.Store.All<IdempotencyRecord>().Any(x => x.Key == key),
                    "Rejected line persisted a request receipt");
            }

            var control = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner,
                Fixture.Key(), new("GL03-CONTROL-TEST", "حساب رقابي", AccountType.Asset,
                    null, true, true, false, null, NormalBalance.Debit), default);
            // The preceding account creation is an expected audited master-data operation.
            initialAudit = f.Store.Count<AuditEvent>();
            initialReceipts = f.Store.Count<IdempotencyRecord>();
            var invalid = f.Request();
            invalid.Lines[0] = invalid.Lines[0] with { AccountId = control.ResourceId };
            var deniedKey = Fixture.Key();
            await Reject("control_account_protected", () => f.Draft(invalid, deniedKey));
            Ensure(!f.Store.All<IdempotencyRecord>().Any(x => x.Key == deniedKey) &&
                f.Store.Count<JournalEntry>() == initialJournals &&
                f.Store.Count<AuditEvent>() == initialAudit &&
                f.Store.Count<IdempotencyRecord>() == initialReceipts &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == initialNumbers &&
                f.Store.Count<OutboxEvent>() == 0,
                "GL03 rejected sides/control account leaked a journal, numbering, receipt, audit or outbox");
            var valid = await f.Draft();
            Ensure((await f.Journal(valid.ResourceId)).State == JournalState.Draft &&
                valid.Number == "JV-2026-000001", "Rejected lines consumed a document number");
        });
    }

    public static async Task RunSql(string connection, Guid cid, Actor clerk,
        Actor owner, CreateJournalRequest example)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Context();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var control = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("SQL-GL03-CONTROL", "حساب رقابي SQL", AccountType.Asset, null,
                true, true, false, null, NormalBalance.Debit), default));
        int journals, audits, receipts, outbox;
        long numbers;
        await using (var db = Context())
        {
            journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            numbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                .SumAsync(x => x.LastNumber);
        }

        var samples = new (decimal Debit, decimal Credit, string Code)[]
        {
            (40m, 40m, "invalid_journal_side"),
            (0m, 0m, "invalid_journal_side"),
            (-1m, 0m, "invalid_amount")
        };
        foreach (var (debit, credit, expected) in samples)
        {
            var malformed = example with
            {
                Lines = example.Lines.Select((x, i) => i == 0
                    ? x with { Debit = debit, Credit = credit } : x).ToArray()
            };
            var key = Fixture.Key();
            await Reject(expected, () => Call(s => s.Journal.CreateJournal(cid, clerk,
                key, malformed, default)));
        }
        var directControl = example with
        {
            Lines = example.Lines.Select((x, i) => i == 0
                ? x with { AccountId = control.ResourceId } : x).ToArray()
        };
        var forbiddenKey = Fixture.Key();
        await Reject("control_account_protected", () => Call(s =>
            s.Journal.CreateJournal(cid, clerk, forbiddenKey, directControl, default)));

        await using (var db = Context())
        {
            Ensure(await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == receipts &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == outbox &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid &&
                    x.Key == forbiddenKey) &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber) == numbers,
                "Rejected GL03 invalid sides/control SQL request was not atomic");
        }
        // A valid balanced manual journal must remain available after the rejected attempts.
        var accepted = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(),
            example, default));
        var journal = await Call(s => s.Journal.GetJournal(cid, accepted.ResourceId,
            clerk, default));
        Ensure(journal.State == JournalState.Draft &&
            journal.Lines.Sum(x => x.FunctionalDebit) == journal.Lines.Sum(x => x.FunctionalCredit),
            "Valid GL03 journal was rejected after invalid SQL attempts");
        Console.WriteLine("  SQL PASS GL03-T02 rejects both-positive/zero/negative sides and direct control with atomic no-effects; valid manual draft still works");
    }

    private static async Task Reject(string expected, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == expected) { return; }
        throw new Exception("Expected GL03-T02 rejection code " + expected);
    }
    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
