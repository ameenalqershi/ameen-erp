namespace Ameen.Erp.Domain;

// CASH05 structural validation only: does not authorize transfers, select
// ledger accounts, calculate FX differences, move balances or create journals.
public enum CashTransferAccountKind { CashBox, BankAccount }

public readonly record struct CashTransferAccountRef(CashTransferAccountKind Kind, Guid Id);

public static class CashTransferValidation
{
    public static void Validate(CashTransferAccountRef source, CashTransferAccountRef destination,
        string sourceCurrency, string destinationCurrency, decimal sourceAmount,
        decimal destinationAmount, decimal sourceExchangeRate, decimal destinationExchangeRate,
        decimal? fee)
    {
        Check.That(Enum.IsDefined(source.Kind) && Enum.IsDefined(destination.Kind) &&
            source.Id != Guid.Empty && destination.Id != Guid.Empty,
            "cash_transfer_account_required", "الحساب المرسل والمستلم مطلوبان.");
        Check.That(source != destination, "cash_transfer_same_account",
            "لا يجوز التحويل من الحساب نفسه وإليه.");
        Check.Currency(sourceCurrency);
        Check.Currency(destinationCurrency);
        Check.Amount(sourceAmount, positive: true);
        Check.Amount(destinationAmount, positive: true);
        Check.That(ValidRate(sourceExchangeRate) && ValidRate(destinationExchangeRate),
            "invalid_exchange_rate", "يجب توثيق سعر صرف صالح لكل طرف.");
        if (fee.HasValue) Check.Amount(fee.Value);
    }

    private static bool ValidRate(decimal rate) =>
        rate > 0 && rate < 10_000_000_000m && decimal.Round(rate, 10) == rate;
}
