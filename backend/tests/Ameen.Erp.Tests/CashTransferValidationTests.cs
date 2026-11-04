using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CashTransferValidationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
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
