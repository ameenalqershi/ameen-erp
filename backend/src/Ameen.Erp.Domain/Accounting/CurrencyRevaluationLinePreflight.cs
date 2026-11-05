namespace Ameen.Erp.Domain;

// GL08-F09/F10/F11 are system-computed Decimal(20,6) amounts.
// This pure calculation does not choose a rate, assess an eligible monetary
// item, pick gain/loss accounts, or authorize a revaluation/posting cycle.
public enum CurrencyRevaluationLineStatus
{
    Valid,
    InvalidBookValue,
    InvalidNewValue,
    InvalidDifference
}

public sealed record CurrencyRevaluationLineAmounts(decimal BookValue,
    decimal NewValue, decimal Difference);

public static class CurrencyRevaluationLinePreflight
{
    private const decimal Limit = 100_000_000_000_000m;

    // The source values must already have been normalized using the approved
    // currency's precision. Never silently round a financial amount here.
    private static bool IsDecimal20Scale6(decimal value) =>
        value > -Limit && value < Limit && decimal.Round(value, 6) == value;

    public static CurrencyRevaluationLineStatus Calculate(decimal bookValue,
        decimal newValue, out CurrencyRevaluationLineAmounts? line)
    {
        line = null;
        if (!IsDecimal20Scale6(bookValue))
            return CurrencyRevaluationLineStatus.InvalidBookValue;
        if (!IsDecimal20Scale6(newValue))
            return CurrencyRevaluationLineStatus.InvalidNewValue;

        decimal difference;
        try { difference = checked(newValue - bookValue); }
        catch (OverflowException) { return CurrencyRevaluationLineStatus.InvalidDifference; }
        if (!IsDecimal20Scale6(difference))
            return CurrencyRevaluationLineStatus.InvalidDifference;

        line = new CurrencyRevaluationLineAmounts(bookValue, newValue, difference);
        return CurrencyRevaluationLineStatus.Valid;
    }
}
