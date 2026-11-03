using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH10-01 only: no statement import, matching transaction, or GL posting.
public static class BankReconciliationFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH10-01: bank period, signed balances and match fields retain exact values", f =>
        {
            var now = new FixedClock().UtcNow;
            var start = new DateOnly(2026, 10, 1);
            var end = new DateOnly(2026, 10, 31);
            var bank = new BankReconciliation(f.CompanyId, f.BranchId, "Unreviewed",
                Guid.NewGuid(), start, end, -120.500001m, 400.123456m,
                "statement-0001", f.Owner.Subject, now);
            var line = new BankReconciliationLine(f.CompanyId, bank.Id,
                Guid.NewGuid(), Guid.NewGuid(), 25.000001m, "unexplained");
            Ensure(bank.Id != Guid.Empty && bank.CompanyId == f.CompanyId &&
                bank.BranchId == f.BranchId && bank.State == "Unreviewed" &&
                bank.StatementFrom == start && bank.StatementTo == end &&
                bank.OpeningBalance == -120.500001m && bank.ClosingBalance == 400.123456m &&
                bank.StatementFileId == "statement-0001" &&
                bank.CreatedBy == f.Owner.Subject && bank.CreatedAt == now &&
                bank.LastModifiedAt == now && bank.Revision == 1 &&
                line.CompanyId == f.CompanyId && line.ReconciliationId == bank.Id &&
                line.BankMovementId != Guid.Empty && line.LedgerMovementId.HasValue &&
                line.MatchedAmount == 25.000001m && line.VarianceReason == "unexplained" &&
                f.Store.All<JournalEntry>().Count() == 0);
            return Task.CompletedTask;
        });

        test("CASH10-01: optional branch, file, ledger movement, difference explanation", f =>
        {
            var now = new FixedClock().UtcNow;
            var date = new DateOnly(2026, 11, 3);
            var bank = new BankReconciliation(f.CompanyId, null, "Unreviewed",
                Guid.NewGuid(), date, date, -1m, 0m, null, f.Owner.Subject, now);
            var line = new BankReconciliationLine(f.CompanyId, bank.Id,
                Guid.NewGuid(), null, 0m, null);
            Ensure(bank.BranchId == null && bank.StatementFileId == null &&
                line.LedgerMovementId == null && line.VarianceReason == null &&
                line.MatchedAmount == 0m);
            return Task.CompletedTask;
        });

        test("CASH10-01: invalid dates, references, precision and negative match fail", f =>
        {
            var now = new FixedClock().UtcNow;
            var date = new DateOnly(2026, 11, 3);
            BankReconciliation Make(Guid? branch, Guid account, DateOnly from,
                DateOnly to, decimal opening, decimal closing) =>
                new(f.CompanyId, branch, "Unreviewed", account, from, to,
                    opening, closing, null, f.Owner.Subject, now);
            Reject("bank_reconciliation_reference_invalid",
                () => Make(Guid.Empty, Guid.NewGuid(), date, date, 1m, 1m));
            Reject("bank_reconciliation_reference_invalid",
                () => Make(null, Guid.Empty, date, date, 1m, 1m));
            Reject("bank_reconciliation_date_invalid",
                () => Make(null, Guid.NewGuid(), date, date.AddDays(-1), 1m, 1m));
            Reject("bank_reconciliation_date_invalid",
                () => Make(null, Guid.NewGuid(), default, date, 1m, 1m));
            Reject("invalid_amount",
                () => Make(null, Guid.NewGuid(), date, date, -1.0000001m, 1m));
            Reject("invalid_amount",
                () => Make(null, Guid.NewGuid(), date, date, 1m, 100_000_000_000_000m));
            Reject("bank_reconciliation_line_reference_invalid",
                () => new BankReconciliationLine(f.CompanyId, Guid.Empty,
                    Guid.NewGuid(), null, 1m, null));
            Reject("bank_reconciliation_line_reference_invalid",
                () => new BankReconciliationLine(f.CompanyId, Guid.NewGuid(),
                    Guid.Empty, null, 1m, null));
            Reject("bank_reconciliation_line_reference_invalid",
                () => new BankReconciliationLine(f.CompanyId, Guid.NewGuid(),
                    Guid.NewGuid(), Guid.Empty, 1m, null));
            Reject("invalid_amount",
                () => new BankReconciliationLine(f.CompanyId, Guid.NewGuid(),
                    Guid.NewGuid(), null, -1m, null));
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH10 must reject " + code);
    }

    private static void Ensure(bool condition)
    {
        if (!condition) throw new Exception("CASH10 foundation assertion failed");
    }
}
