namespace Ameen.Erp.Domain;

// Pure, conservative SYS09 preflight. It does NOT prove snapshot completeness,
// role eligibility, approved document kinds, or transaction-safe uniqueness.
// Consequently it must not be used as the sole guard for any database write.
public enum ApprovalRouteConflictStatus
{
    Clear,
    Overlap,
    UnresolvedCurrency,
    InvalidSnapshot
}

public static class ApprovalRouteConflictPolicy
{
    public static ApprovalRouteConflictStatus Evaluate(ApprovalRoute? candidate,
        IEnumerable<ApprovalRoute>? tenantRoutes)
    {
        if (candidate is null || tenantRoutes is null || !Valid(candidate))
            return ApprovalRouteConflictStatus.InvalidSnapshot;

        var snapshot = tenantRoutes.ToArray();
        // Callers must supply a complete tenant-scoped snapshot. Foreign/null or
        // duplicate records cannot safely establish absence of conflicts.
        if (snapshot.Any(x => x is null || !Valid(x) || x.CompanyId != candidate.CompanyId) ||
            snapshot.Select(x => x.Id).Distinct().Count() != snapshot.Length ||
            !DistinctStageIdentities(candidate, snapshot))
            return ApprovalRouteConflictStatus.InvalidSnapshot;

        var unresolvedCurrency = false;
        foreach (var other in snapshot)
        {
            if (other.Id == candidate.Id || !other.IsActive ||
                other.DocumentType != candidate.DocumentType ||
                (candidate.BranchId is not null && other.BranchId is not null &&
                 candidate.BranchId != other.BranchId))
                continue;

            // No approved FX conversion/route comparison policy exists. Do not
            // compare raw numbers in different currencies or assume no conflict.
            if (other.CurrencyId != candidate.CurrencyId)
            {
                unresolvedCurrency = true;
                continue;
            }

            // Both bounds are inclusive. Null maximum means unbounded.
            if (candidate.MinimumAmount <= (other.MaximumAmount ?? decimal.MaxValue) &&
                other.MinimumAmount <= (candidate.MaximumAmount ?? decimal.MaxValue))
                return ApprovalRouteConflictStatus.Overlap;
        }
        return unresolvedCurrency ? ApprovalRouteConflictStatus.UnresolvedCurrency :
            ApprovalRouteConflictStatus.Clear;
    }

    // Stage IDs are globally unique persistence identities, not merely unique
    // inside each route. A duplicate across different routes means the supplied
    // snapshot is corrupt. Ignore the persisted copy of the candidate itself:
    // edit preflight may legitimately supply the same route twice (by ID).
    private static bool DistinctStageIdentities(ApprovalRoute candidate,
        IEnumerable<ApprovalRoute> snapshot)
    {
        var ids = new HashSet<Guid>(candidate.Stages.Select(x => x.Id));
        foreach (var route in snapshot)
        {
            if (route.Id == candidate.Id) continue;
            foreach (var stage in route.Stages)
                if (!ids.Add(stage.Id)) return false;
        }
        return true;
    }

    private static bool Valid(ApprovalRoute route)
    {
        const decimal limit = 99999999999999.999999m;
        var type = route.DocumentType;
        return route.Id != Guid.Empty && route.CompanyId != Guid.Empty &&
            // Audit fields may be corrupted in a hydrated snapshot even when
            // the amount and stage dimensions are otherwise well formed.
            route.Revision > 0 && route.CreatedAt != default &&
            route.LastModifiedAt >= route.CreatedAt &&
            !string.IsNullOrWhiteSpace(route.CreatedBy) &&
            route.CreatedBy.Length <= 200 && route.CreatedBy == route.CreatedBy.Trim() &&
            route.BranchId != Guid.Empty && route.CurrencyId != Guid.Empty &&
            !string.IsNullOrEmpty(type) && type.Length <= 32 &&
            type[0] is >= 'A' and <= 'Z' or >= '0' and <= '9' &&
            type.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-') &&
            route.MinimumAmount >= 0 && route.MinimumAmount <= limit &&
            decimal.Round(route.MinimumAmount, 6) == route.MinimumAmount &&
            (route.MaximumAmount is null || route.MaximumAmount >= route.MinimumAmount &&
             route.MaximumAmount <= limit &&
             decimal.Round(route.MaximumAmount.Value, 6) == route.MaximumAmount) &&
            ValidStages(route);
    }

    // A malformed hydrated stage must not let a corrupt route silently disappear
    // from the conflict preflight. Empty stages remain a separate, unaccepted
    // activation policy: this preflight never authorizes persistence.
    private static bool ValidStages(ApprovalRoute route)
    {
        if (route.Stages is null) return false;
        var numbers = new HashSet<int>();
        var ids = new HashSet<Guid>();
        foreach (var stage in route.Stages)
        {
            if (stage is null || stage.Id == Guid.Empty ||
                stage.CompanyId != route.CompanyId || stage.ApprovalRouteId != route.Id ||
                stage.ApproverRoleId == Guid.Empty || stage.StageNumber <= 0 ||
                stage.AlertAfterMinutes < 0 || !numbers.Add(stage.StageNumber) ||
                !ids.Add(stage.Id)) return false;
        }
        return true;
    }

}
