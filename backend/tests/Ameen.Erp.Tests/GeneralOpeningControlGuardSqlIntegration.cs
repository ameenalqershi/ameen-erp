using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL02-C01 accepted scope: direct control-account opening must be denied.
// This does not claim SAL/PUR subsidiary reconciliation or zero clearing.
public static class GeneralOpeningControlGuardSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk,
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

        var control = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("SQL-GL02-CTRL-GUARD", "حساب رقابي افتتاحي SQL", AccountType.Asset,
                null, true, true, false, null, NormalBalance.Debit), default));

        async Task<(int Journals, int Audit, int Receipts, int Outbox, long Numbers)> Snapshot()
        {
            await using var db = Context();
            return (
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber));
        }

        var original = GeneralOpeningTests.Request(baseRequest);
        var invalid = original with
        {
            ExternalReference = "GL02-CTRL-DENIED-" + Fixture.Key(),
            Lines = original.Lines.Select((line, index) => index == 0
                ? line with { AccountId = control.ResourceId } : line).ToArray()
        };
        var before = await Snapshot();
        var deniedKey = Fixture.Key();
        try
        {
            await Call(s => s.Journal.CreateGeneralOpening(cid, clerk, deniedKey, invalid, default));
            throw new Exception("GL02-C01 direct control-account opening unexpectedly accepted");
        }
        catch (BusinessException e) when (e.Code == "control_account_protected") { }

        Ensure(before == await Snapshot(),
            "GL02-C01 rejected control opening changed journals, audit, receipt, outbox or document numbers");
        await using (var db = Context())
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x =>
                x.CompanyId == cid && x.Key == deniedKey),
                "GL02-C01 denied opening retained an idempotency receipt");

        var valid = await Call(s => s.Journal.CreateGeneralOpening(cid, clerk,
            Fixture.Key(), original with { ExternalReference = "GL02-VALID-" + Fixture.Key() }, default));
        var saved = await Call(s => s.Journal.GetGeneralOpening(cid, valid.ResourceId, clerk, default));
        Ensure(saved.State == JournalState.Draft &&
            saved.Source == JournalSource.GeneralOpening &&
            saved.Lines.Sum(x => x.FunctionalDebit) == saved.Lines.Sum(x => x.FunctionalCredit) &&
            saved.Lines.All(x => x.AccountId != control.ResourceId),
            "GL02-C01 control guard prevented legitimate balanced non-control general opening");
        Console.WriteLine("  SQL PASS GL02-C01 protected control opening atomically refused; no receipt/number/audit/outbox, valid non-control accepted");
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
