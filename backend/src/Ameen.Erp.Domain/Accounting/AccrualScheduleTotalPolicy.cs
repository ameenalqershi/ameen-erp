namespace Ameen.Erp.Domain;

// GL09-C01: independently specified schedule arithmetic only.
// This is not a complete GL09 accrual, deferral or posting policy. In particular,
// the GL09 schedule-type catalogue and lifecycle/approval contract are unresolved.
public sealed record AccrualSchedulePeriod(Guid FiscalPeriodId, decimal ScheduledAmount);

public enum AccrualScheduleTotalStatus
{
    Balanced,
    InvalidSnapshot,
    DuplicatePeriod,
    ContractTotalMismatch
}

public static class AccrualScheduleTotalPolicy
{
    // GL09-F14/F20 data dictionary: Decimal(20,6) is the internal storage
    // shape. Do not silently round a schedule line or contract total while
    // the currency-specific rounding policy and operational route are missing.
    private const decimal StorageLimit = 100_000_000_000_000m;
    private static bool FitsDecimal20Scale6(decimal amount) =>
        amount > -StorageLimit && amount < StorageLimit &&
        decimal.Round(amount, 6) == amount;

    // Only the original two invariant checks are implemented:
    // every schedule period appears once, and the exact sum equals the contract.
    // A complete tenant-scoped period/account snapshot must be validated by
    // the future operational service; this pure helper cannot authorize writes.
    public static AccrualScheduleTotalStatus Evaluate(decimal contractTotal,
        IEnumerable<AccrualSchedulePeriod>? scheduledPeriods)
    {
        if (scheduledPeriods is null || !FitsDecimal20Scale6(contractTotal))
            return AccrualScheduleTotalStatus.InvalidSnapshot;

        var periods = scheduledPeriods.ToArray();
        if (periods.Length == 0 || periods.Any(p => p is null ||
                p.FiscalPeriodId == Guid.Empty ||
                !FitsDecimal20Scale6(p.ScheduledAmount)))
            return AccrualScheduleTotalStatus.InvalidSnapshot;

        var ids = new HashSet<Guid>();
        decimal sum = 0m;
        foreach (var period in periods)
        {
            if (!ids.Add(period.FiscalPeriodId))
                return AccrualScheduleTotalStatus.DuplicatePeriod;

            try { sum = checked(sum + period.ScheduledAmount); }
            catch (OverflowException)
            {
                return AccrualScheduleTotalStatus.InvalidSnapshot;
            }
        }

        return sum == contractTotal
            ? AccrualScheduleTotalStatus.Balanced
            : AccrualScheduleTotalStatus.ContractTotalMismatch;
    }
}
