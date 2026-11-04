namespace Ameen.Erp.Domain;

// GL10-C01 is a read-only comparison gate, not an ERP subledger reconciliation
// implementation. A documented explanation cannot make an unequal balance
// appear reconciled; no balancing journal is ever authorized by this result.
public enum ControlReconciliationStatus
{
    Matched,
    UnexplainedDifference,
    ExplainedDifferenceStillOpen
}

public static class ControlReconciliationPolicy
{
    // Only compares already obtained balances. The GL10 subledger catalogue,
    // source-ledger ownership, reconciliation dates, balance sign convention
    // and journal authority remain unapproved/absent from operational services.
    public static ControlReconciliationStatus Evaluate(decimal ledgerBalance,
        decimal detailBalance, string? differenceReason)
    {
        if (ledgerBalance == detailBalance)
            return ControlReconciliationStatus.Matched;

        return string.IsNullOrWhiteSpace(differenceReason)
            ? ControlReconciliationStatus.UnexplainedDifference
            : ControlReconciliationStatus.ExplainedDifferenceStillOpen;
    }
}
