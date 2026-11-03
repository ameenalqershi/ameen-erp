using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL03-T01: a balanced manual journal must create exactly one durable
// accounting effect in SQL, the account ledger, trial balance, audit and
// outbox. Use a unique document reference to avoid other SQL acceptance
// scenarios and compare the trial balance before/after this single posting.
public static class BalancedJournalLedgerSqlAcceptance
{
    public static async Task Run(string connection, Guid cid, Actor clerk,
        Actor approver, CreateJournalRequest sample)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Db();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        FiscalPeriod period;
        await using (var db = Db())
            period = await db.Set<FiscalPeriod>().AsNoTracking()
                .Where(x => x.CompanyId == cid && !x.IsClosed)
                .OrderBy(x => x.StartsOn).FirstAsync();

        var date = period.StartsOn;
        var reference = "GL03-T01-" + Guid.NewGuid().ToString("N")[..12];
        var request = sample with
        {
            DocumentDate = date, AccountingDate = date,
            ExternalReference = reference,
            Description = "قيد قبول متزن في الأستاذ"
        };
        var debitAccount = sample.Lines[0].AccountId;
        var creditAccount = sample.Lines[1].AccountId;
        var amount = sample.Lines[0].Debit;
        Ensure(amount > 0 && sample.Lines[1].Credit == amount,
            "GL03-T01 fixture must be a balanced manual journal");

        Task<TrialBalanceResult> Trial() => Call(s =>
            s.Report.TrialBalance(cid, clerk, date, date, sample.BranchId,
                default, null, null, false));
        Task<AccountLedgerResult> Ledger(Guid accountId) => Call(s =>
            s.Report.AccountLedger(cid, accountId, clerk, date, date,
                sample.BranchId, 0, 10, default, null, reference));

        var before = await Trial();
        var noLedger = await Ledger(debitAccount);
        Ensure(noLedger.Entries.TotalCount == 0 &&
            noLedger.ClosingBalance == 0 &&
            noLedger.PeriodDebit == 0,
            "GL03-T01 unique reference unexpectedly matched an existing posting");
        int originalAudit, originalOutbox, originalReceipt;
        await using (var db = Db())
        {
            originalAudit = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            originalOutbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            originalReceipt = await db.Set<IdempotencyRecord>()
                .CountAsync(x => x.CompanyId == cid);
        }

        var created = await Call(s => s.Journal.CreateJournal(cid, clerk,
            Fixture.Key(), request, default));
        var submitted = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, clerk, Fixture.Key(), new(created.Version), "submit", default));
        var approved = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, approver, Fixture.Key(), new(submitted.Version), "approve", default));
        var postKey = Fixture.Key();
        var posted = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, approver, postKey, new(approved.Version), "post", default));
        var replay = await Call(s => s.Journal.TransitionJournal(cid,
            created.ResourceId, approver, postKey, new(approved.Version), "post", default));
        Ensure(posted == replay && posted.Status == nameof(JournalState.Posted),
            "GL03-T01 posting replay failed to preserve its original receipt");

        var debit = await Ledger(debitAccount);
        var credit = await Ledger(creditAccount);
        var after = await Trial();
        Ensure(debit.CompanyId == cid && debit.BranchIds.SequenceEqual(new[] { sample.BranchId }) &&
            debit.Entries.TotalCount == 1 && debit.Entries.Items.Count == 1 &&
            debit.Entries.Items[0].JournalId == posted.ResourceId &&
            debit.Entries.Items[0].Number == posted.Number &&
            debit.Entries.Items[0].AccountingDate == date &&
            debit.Entries.Items[0].Debit == amount &&
            debit.Entries.Items[0].Credit == 0 &&
            debit.Entries.Items[0].OriginalDebit == amount &&
            debit.Entries.Items[0].CurrencyCode == "SAR" &&
            debit.OpeningBalance == 0 && debit.ClosingBalance == amount &&
            debit.PeriodDebit == amount && debit.PeriodCredit == 0 &&
            credit.Entries.TotalCount == 1 && credit.Entries.Items.Count == 1 &&
            credit.Entries.Items[0].JournalId == posted.ResourceId &&
            credit.Entries.Items[0].Debit == 0 &&
            credit.Entries.Items[0].Credit == amount &&
            credit.ClosingBalance == -amount &&
            credit.PeriodCredit == amount &&
            after.TotalPeriodDebit - before.TotalPeriodDebit == amount &&
            after.TotalPeriodCredit - before.TotalPeriodCredit == amount,
            "GL03-T01 balanced journal failed to produce equal SQL ledger and trial-balance effects");

        await using (var db = Db())
        {
            var journal = await db.Set<JournalEntry>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == posted.ResourceId);
            var auditDelta = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) - originalAudit;
            var postActions = await db.Set<AuditEvent>().CountAsync(x =>
                x.CompanyId == cid && x.ResourceId == posted.ResourceId && x.Action == "journal.post");
            var outboxDelta = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) - originalOutbox;
            var receiptsDelta = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) - originalReceipt;
            Ensure(journal.State == JournalState.Posted &&
                journal.PostedAt is not null &&
                journal.ExternalReference == reference &&
                journal.BranchId == sample.BranchId &&
                auditDelta == 5 && postActions == 1 && outboxDelta == 1 &&
                receiptsDelta == 4,
                $"GL03-T01 atomicity mismatch: state={journal.State}, postedAt={journal.PostedAt}, " +
                $"referenceMatch={journal.ExternalReference == reference}, branchMatch={journal.BranchId == sample.BranchId}, " +
                $"auditDelta={auditDelta}, postActions={postActions}, outboxDelta={outboxDelta}, receiptsDelta={receiptsDelta}");
        }

        await Expect("permission_denied", () => Call(s =>
            s.Report.AccountLedger(cid, debitAccount, new Actor("gl03-t01-outsider"),
                date, date, sample.BranchId, 0, 10, default, null, reference)));
        Console.WriteLine("  SQL PASS GL03-T01 posted balanced journal exactly once in both account ledgers, trial balance, audit and outbox; denied cross-membership read");
    }

    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected GL03-T01 authorization error " + code);
    }
    private static void Ensure(bool ok, string error)
    {
        if (!ok) throw new Exception(error);
    }
}
