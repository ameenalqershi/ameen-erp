namespace Ameen.Erp.Domain;

// SAL13-01: read-only report filter boundary. This does not fetch records or
// grant access: callers must obtain authorizedBranches from validated SYS access.
// Status, display mode and detail level are opaque pending approved catalogs.
public sealed class SalesMovementReportFilter
{
    public Guid CompanyId { get; }
    public IReadOnlyList<Guid> BranchIds { get; }
    public DateOnly FromDate { get; }
    public DateOnly ToDate { get; }
    public Guid DisplayCurrencyId { get; }
    public string CurrencyDisplayMode { get; }
    public string DocumentStatus { get; }
    public string DetailLevel { get; }
    public Guid? CustomerId { get; }
    public Guid? SalespersonId { get; }
    public Guid? ItemId { get; }
    public string? SaleKind { get; }

    public SalesMovementReportFilter(
        Guid companyId, IEnumerable<Guid> authorizedBranches,
        IEnumerable<Guid> requestedBranches,
        DateOnly fromDate, DateOnly toDate, Guid displayCurrencyId,
        string currencyDisplayMode, string documentStatus, string detailLevel,
        Guid? customerId = null, Guid? salespersonId = null,
        Guid? itemId = null, string? saleKind = null)
    {
        Check.That(companyId != Guid.Empty && displayCurrencyId != Guid.Empty,
            "sal13_reference_required", "الشركة وعملة العرض مطلوبتان.");
        Check.That(fromDate != default && toDate != default && toDate >= fromDate,
            "sal13_date_range_invalid", "نطاق تاريخ التقرير غير صالح.");
        Check.That(customerId != Guid.Empty && salespersonId != Guid.Empty &&
            itemId != Guid.Empty, "sal13_reference_invalid", "مرجع المرشح غير صالح.");
        Check.That(authorizedBranches is not null && requestedBranches is not null,
            "sal13_branch_scope_required", "يجب تحديد نطاق الفروع المصرح.");
        var authorized = authorizedBranches!.ToHashSet();
        var requested = requestedBranches!.Distinct().ToArray();
        Check.That(authorized.Count > 0 && !authorized.Contains(Guid.Empty) &&
            requested.Length > 0 && !requested.Contains(Guid.Empty),
            "sal13_branch_scope_required", "نطاق التقرير يتطلب فروعًا صالحة.");
        Check.That(requested.All(authorized.Contains),
            "sal13_branch_scope_denied", "لا يجوز عرض فرع خارج التفويض.");
        var mode = Check.Text(currencyDisplayMode, "طريقة عرض العملات", 64);
        var status = Check.Text(documentStatus, "حالة المستند", 64);
        var level = Check.Text(detailLevel, "مستوى التفصيل", 64);
        var kind = saleKind is null ? null : Check.Text(saleKind, "نوع البيع", 64);

        CompanyId = companyId;
        BranchIds = Array.AsReadOnly(requested);
        FromDate = fromDate;
        ToDate = toDate;
        DisplayCurrencyId = displayCurrencyId;
        CurrencyDisplayMode = mode;
        DocumentStatus = status;
        DetailLevel = level;
        CustomerId = customerId;
        SalespersonId = salespersonId;
        ItemId = itemId;
        SaleKind = kind;
    }
}

// SAL13 report projections must not reveal cost or margin to unauthorized users.
// This function is a data redaction helper, not a substitute for SYS permissions,
// DB tenant predicates or the SQL/HTTP authorization enforcement that remains pending.
public static class SalesMovementReportVisibility
{
    public static SalesMovementReportRow ForAuthorizedViewer(
        SalesMovementReportSourceRow source, bool canViewCostAndProfit)
    {
        Check.That(source is not null, "sal13_source_required", "صف التقرير مطلوب.");
        var approvedSource = source!; // Check.That above rejects missing source.
        return new SalesMovementReportRow(approvedSource.DocumentId,
            approvedSource.GrossSales, approvedSource.ReturnsAmount, approvedSource.NetSales,
            canViewCostAndProfit ? approvedSource.Cost : null,
            canViewCostAndProfit ? approvedSource.Profit : null);
    }
}

// Amounts are supplied by an approved report calculation. No new netting,
// return or cost basis policy is introduced in this structural feature.
public sealed record SalesMovementReportSourceRow(
    Guid DocumentId, decimal GrossSales, decimal ReturnsAmount,
    decimal NetSales, decimal? Cost, decimal? Profit);

public sealed record SalesMovementReportRow(
    Guid DocumentId, decimal GrossSales, decimal ReturnsAmount,
    decimal NetSales, decimal? Cost, decimal? Profit);
