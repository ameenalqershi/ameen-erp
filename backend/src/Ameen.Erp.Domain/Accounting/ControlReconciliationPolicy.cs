namespace Ameen.Erp.Domain;

// GL10-C01 is a read-only comparison gate, not an ERP subledger reconciliation
// implementation. A documented explanation cannot make an unequal balance
// appear reconciled; no balancing journal is ever authorized by this result.
public enum ControlReconciliationStatus
{
    InvalidSnapshot,
    Matched,
    UnexplainedDifference,
    ExplainedDifferenceStillOpen
}

public static class ControlReconciliationPolicy
{
    // GL10-F07/F08/F09 are Decimal(20,6) in the original field dictionary.
    // Reject values requiring rounding and a difference that cannot be stored.
    // Currency-specific rounding is intentionally not inferred here.
    private const decimal StorageLimit = 100_000_000_000_000m;
    private static bool FitsDecimal20Scale6(decimal amount) =>
        amount > -StorageLimit && amount < StorageLimit &&
        decimal.Round(amount, 6) == amount;

    // Only compares already obtained balances. The GL10 subledger catalogue,
    // source-ledger ownership, reconciliation dates, balance sign convention
    // and journal authority remain unapproved/absent from operational services.
    public static ControlReconciliationStatus Evaluate(decimal ledgerBalance,
        decimal detailBalance, string? differenceReason)
    {
        if (!FitsDecimal20Scale6(ledgerBalance) || !FitsDecimal20Scale6(detailBalance))
            return ControlReconciliationStatus.InvalidSnapshot;

        decimal difference;
        try { difference = checked(ledgerBalance - detailBalance); }
        catch (OverflowException) { return ControlReconciliationStatus.InvalidSnapshot; }
        if (!FitsDecimal20Scale6(difference))
            return ControlReconciliationStatus.InvalidSnapshot;
        if (difference == 0m)
            return ControlReconciliationStatus.Matched;

        return string.IsNullOrWhiteSpace(differenceReason)
            ? ControlReconciliationStatus.UnexplainedDifference
            : ControlReconciliationStatus.ExplainedDifferenceStillOpen;
    }
}
