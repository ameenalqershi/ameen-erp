namespace Ameen.Erp.Domain;

// GL14 structural preflight only. UnresolvedPolicy is NOT permission to create,
// activate, post, or use a document type: SYS09 kinds/FX/roles and SYS10 policy
// are not approved or checked by this pure function.
public enum JournalDocumentTypeReferenceStatus
{
    InvalidSnapshot,
    InactiveReference,
    UnresolvedPolicy
}

public static class JournalDocumentTypeReferencePolicy
{
    public static JournalDocumentTypeReferenceStatus Evaluate(JournalDocumentType? card,
        DocumentNumberSeries? series, ApprovalRoute? route)
    {
        if (card is null || series is null || route is null ||
            !ValidCard(card) || !ValidSeries(series) ||
            !ApprovalRouteConflictPolicy.HasValidSnapshot(route) ||
            series.Id != card.NumberSeriesId || route.Id != card.ApprovalRouteId ||
            series.CompanyId != card.CompanyId || route.CompanyId != card.CompanyId ||
            !CompatibleBranch(card.BranchId, series.BranchId) ||
            !CompatibleBranch(card.BranchId, route.BranchId))
            return JournalDocumentTypeReferenceStatus.InvalidSnapshot;

        if (!card.IsActive || !series.IsActive || !route.IsActive)
            return JournalDocumentTypeReferenceStatus.InactiveReference;

        // There is no approved mapping between the GL14 code, SYS10 numbering
        // kind, and SYS09 document kind. No successful authorization result.
        return JournalDocumentTypeReferenceStatus.UnresolvedPolicy;
    }

    private static bool CompatibleBranch(Guid? cardBranch, Guid? referenceBranch) =>
        referenceBranch is null || cardBranch is not null && cardBranch == referenceBranch;

    private static bool ValidCard(JournalDocumentType card) =>
        card.Id != Guid.Empty && card.CompanyId != Guid.Empty &&
        card.BranchId != Guid.Empty && card.NumberSeriesId != Guid.Empty &&
        card.ApprovalRouteId != Guid.Empty && Enum.IsDefined(card.Origin) &&
        ValidCode(card.Code) && ValidText(card.Name) &&
        ValidText(card.CreatedBy) && card.Revision > 0 &&
        card.CreatedAt != default && card.LastModifiedAt >= card.CreatedAt;

    private static bool ValidSeries(DocumentNumberSeries series) =>
        series.Id != Guid.Empty && series.CompanyId != Guid.Empty &&
        series.BranchId != Guid.Empty && series.FiscalYearId != Guid.Empty &&
        Enum.IsDefined(series.DocumentKind) &&
        series.NextNumber > 0 && series.NextNumber < long.MaxValue &&
        (series.Prefix is null || series.Prefix.Length is > 0 and <= 32 &&
         !series.Prefix.Any(char.IsControl) &&
         !series.Prefix.Contains("..", StringComparison.Ordinal)) &&
        ValidText(series.CreatedBy) && series.Revision > 0 &&
        series.CreatedAt != default && series.LastModifiedAt >= series.CreatedAt;

    private static bool ValidText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
        value == value.Trim() && !value.Any(char.IsControl);

    private static bool ValidCode(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 32 &&
        value[0] is >= 'A' and <= 'Z' or >= '0' and <= '9' &&
        value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
}
