namespace Ameen.Erp.Domain;

// INV15-FG01 only. This is not an executable valuation run.
public sealed class InventoryRevaluationHeader : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public DateOnly FromDate { get; private set; }
    private InventoryRevaluationHeader() { }
    public InventoryRevaluationHeader(Guid companyId, Guid? branchId, string statusCode, DateOnly fromDate)
        : base(companyId)
    {
        Check.That(branchId is null || branchId != Guid.Empty,
            "inventory_document_branch_invalid", "فرع إعادة التقييم غير صالح.");
        Check.That(fromDate != default, "inventory_revaluation_from_date_required",
            "تاريخ بداية إعادة التقييم مطلوب.");
        BranchId = branchId;
        StatusCode = Check.Code(statusCode, "حالة إعادة التقييم", 32);
        FromDate = fromDate;
    }
}
