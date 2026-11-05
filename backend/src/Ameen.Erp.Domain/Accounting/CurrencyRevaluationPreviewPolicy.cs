namespace Ameen.Erp.Domain;

// GL08-C01: pure, read-only preflight of a supplied monetary open-item
// snapshot. Eligibility must be independently verified against approved
// SYS05/SAL11/PUR11 sources before any caller can rely on this result.
public sealed record RevaluationOpenItem(Guid OpenItemId, bool IsVerifiedMonetary,
    decimal BookValue, decimal NewValue);

public sealed record RevaluationDifference(Guid OpenItemId, decimal Difference);

public enum RevaluationPreviewStatus
{
    Valid,
    InvalidSnapshot,
    IneligibleItem,
    DuplicateOpenItem,
    ArithmeticOverflow
}

public sealed record RevaluationPreviewResult(RevaluationPreviewStatus Status,
    IReadOnlyList<RevaluationDifference> Differences)
{
    public bool MayPost => false; // No approved GL08 execution contract exists.
}

public static class CurrencyRevaluationPreviewPolicy
{
    public static RevaluationPreviewResult Evaluate(
        IEnumerable<RevaluationOpenItem>? items)
    {
        static RevaluationPreviewResult Deny(RevaluationPreviewStatus status) =>
            new(status, Array.Empty<RevaluationDifference>());

        if (items is null)
            return Deny(RevaluationPreviewStatus.InvalidSnapshot);

        var snapshot = items.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(x => x is null ||
            x.OpenItemId == Guid.Empty))
            return Deny(RevaluationPreviewStatus.InvalidSnapshot);

        var ids = new HashSet<Guid>();
        var differences = new List<RevaluationDifference>(snapshot.Length);
        foreach (var item in snapshot)
        {
            if (!item.IsVerifiedMonetary)
                return Deny(RevaluationPreviewStatus.IneligibleItem);
            if (!ids.Add(item.OpenItemId))
                return Deny(RevaluationPreviewStatus.DuplicateOpenItem);

            decimal difference;
            try { difference = checked(item.NewValue - item.BookValue); }
            catch (OverflowException)
            {
                return Deny(RevaluationPreviewStatus.ArithmeticOverflow);
            }
            differences.Add(new(item.OpenItemId, difference));
        }

        return new(RevaluationPreviewStatus.Valid, differences.AsReadOnly());
    }
}
