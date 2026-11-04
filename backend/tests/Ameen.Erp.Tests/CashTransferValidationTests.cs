using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CashTransferValidationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH05-FG01: draft header preserves all original transfer fields without posting", f =>
        {
            var now = new FixedClock().UtcNow;
            var a = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            var b = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            var year = Guid.NewGuid();
            var transfer = new CashTransfer(f.CompanyId, f.BranchId, year, "0001",
                new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5), "SAR",
                1m, "نقل", null, "Draft", a, b, 100m, 26.666667m, "USD",
                3.75m, 0m, f.Owner.Subject, now);
            if (!(transfer.CompanyId == f.CompanyId && transfer.BranchId == f.BranchId &&
                transfer.FiscalYearId == year && transfer.Number == "0001" &&
                transfer.DocumentDate == new DateOnly(2026, 11, 4) &&
                transfer.AccountingDate == new DateOnly(2026, 11, 5) &&
                transfer.SourceKind == a.Kind && transfer.SourceAccountId == a.Id &&
                transfer.DestinationKind == b.Kind && transfer.DestinationAccountId == b.Id &&
                transfer.SourceAmount == 100m && transfer.DestinationAmount == 26.666667m &&
                transfer.CurrencyCode == "SAR" && transfer.DestinationCurrencyCode == "USD" &&
                transfer.ExchangeRate == 1m && transfer.DestinationExchangeRate == 3.75m &&
                transfer.Fee == 0m && transfer.CreatedAt == now && transfer.Revision == 1 &&
                transfer.LastModifiedAt == now && transfer.State == "Draft"))
                throw new Exception("CASH05 draft header fields not preserved");
            return Task.CompletedTask;
        });
        test("CASH05-FG02: invalid draft audit, date and self-transfer are rejected", f =>
        {
            var now = new FixedClock().UtcNow;
            var a = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            void RejectDraft(string code, DateOnly date, DateTimeOffset created,
                CashTransferAccountRef destination)
            {
                try
                {
                    _ = new CashTransfer(f.CompanyId, f.BranchId, Guid.NewGuid(),
                        "0002", date, new DateOnly(2026, 11, 5), "SAR", 1m,
                        "نقل", null, "Draft", a, destination, 10m, 10m,
                        "SAR", 1m, null, f.Owner.Subject, created);
                }
                catch (BusinessException ex) when (ex.Code == code) { return; }
                throw new Exception("CASH05 draft expected " + code);
            }
            var b = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            RejectDraft("cash_transfer_date_required", default, now, b);
            RejectDraft("cash_transfer_created_at_required", new DateOnly(2026, 11, 4), default, b);
            RejectDraft("cash_transfer_same_account", new DateOnly(2026, 11, 4), now, a);
            return Task.CompletedTask;
        });
        test("CASH05-AC01: distinct cashbox/bank endpoints and both exchange rates validate", f =>
        {
            var source = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            var target = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            CashTransferValidation.Validate(source, target, "SAR", "USD", 100m, 26.666667m,
                1m, 3.75m, 0.000001m);
            CashTransferValidation.Validate(source, target, "SAR", "SAR", 1m, 1m,
                1m, 1m, null);
            // Identical GUIDs in different entity tables are not the same account.
            CashTransferValidation.Validate(source,
                new CashTransferAccountRef(CashTransferAccountKind.BankAccount, source.Id),
                "SAR", "SAR", 1m, 1m, 1m, 1m, 0m);
            return Task.CompletedTask;
        });
        test("CASH05-AC02: self transfer, missing account and unrecognized endpoint fail", f =>
        {
            var a = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            var b = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            Reject("cash_transfer_same_account", a, a);
            Reject("cash_transfer_account_required", a,
                new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.Empty));
            Reject("cash_transfer_account_required", a,
                new CashTransferAccountRef((CashTransferAccountKind)99, b.Id));
            return Task.CompletedTask;
        });
        test("CASH05-AC03: invalid monetary precision, fee and either rate fail", f =>
        {
            var a = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            var b = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            Reject("invalid_amount", a, b, sourceAmount: 0m);
            Reject("invalid_amount", a, b, destinationAmount: 0.0000001m);
            Reject("invalid_amount", a, b, fee: -1m);
            Reject("invalid_exchange_rate", a, b, sourceRate: 0m);
            Reject("invalid_exchange_rate", a, b, destinationRate: 1.00000000001m);
            return Task.CompletedTask;
        });
        test("CASH05-FG02: caller cannot pre-approve or post a new transfer header", f =>
        {
            var sender = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            var receiver = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            foreach (var forgedState in new[] { "Submitted", "Approved", "Posted" })
            {
                try
                {
                    _ = new CashTransfer(f.CompanyId, f.BranchId, Guid.NewGuid(),
                        "0003", new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5),
                        "SAR", 1m, "نقل", "EXT-003", forgedState, sender,
                        receiver, 100m, 100m, "SAR", 1m, 1m,
                        f.Owner.Subject, new FixedClock().UtcNow);
                }
                catch (BusinessException ex) when (ex.Code == "cash_transfer_draft_only")
                {
                    continue;
                }
                throw new Exception("CASH05 forged initial state was accepted: " + forgedState);
            }
            return Task.CompletedTask;
        });
        test("CASH05-FG01: external reference, fee and creator remain draft metadata only", f =>
        {
            var sender = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
            var receiver = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
            var transfer = new CashTransfer(f.CompanyId, f.BranchId, Guid.NewGuid(),
                "0004", new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5),
                "SAR", 1m, "نقل", "EXT-004", "Draft", sender,
                receiver, 100m, 100m, "SAR", 1m, 2m,
                f.Owner.Subject, new FixedClock().UtcNow);
            if (transfer.ExternalReference != "EXT-004" || transfer.Fee != 2m ||
                transfer.CreatedBy != f.Owner.Subject || transfer.State != "Draft" ||
                transfer.Revision != 1)
                throw new Exception("CASH05 draft metadata changed unexpectedly");
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, CashTransferAccountRef source,
        CashTransferAccountRef destination, decimal sourceAmount = 1m,
        decimal destinationAmount = 1m, decimal sourceRate = 1m,
        decimal destinationRate = 1m, decimal? fee = null)
    {
        try
        {
            CashTransferValidation.Validate(source, destination, "SAR", "SAR",
                sourceAmount, destinationAmount, sourceRate, destinationRate, fee);
        }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH05 expected " + code);
    }
}
