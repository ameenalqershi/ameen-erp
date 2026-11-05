namespace Ameen.Erp.Domain;

// INV08-FG01 only. Remaining document fields and operational transitions are absent.
public sealed class InventoryIssueHeader : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    private InventoryIssueHeader() { }
    public InventoryIssueHeader(Guid companyId, Guid branchId, Guid fiscalYearId) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty,
            "inventory_document_scope_required", "إذن الصرف: الفرع والسنة المالية مطلوبان.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
    }
}
