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
    // Only the original two invariant checks are implemented:
    // every schedule period appears once, and the exact sum equals the contract.
    // A complete tenant-scoped period/account snapshot must be validated by
    // the future operational service; this pure helper cannot authorize writes.
    public static AccrualScheduleTotalStatus Evaluate(decimal contractTotal,
        IEnumerable<AccrualSchedulePeriod>? scheduledPeriods)
    {
        if (scheduledPeriods is null)
            return AccrualScheduleTotalStatus.InvalidSnapshot;

        var periods = scheduledPeriods.ToArray();
        if (periods.Length == 0 || periods.Any(p => p is null ||
                p.FiscalPeriodId == Guid.Empty))
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
