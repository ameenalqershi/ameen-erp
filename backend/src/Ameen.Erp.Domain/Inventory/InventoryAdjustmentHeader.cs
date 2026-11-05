namespace Ameen.Erp.Domain;

// INV13-FG01 only. Remaining document fields and operational transitions are absent.
public sealed class InventoryAdjustmentHeader : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    private InventoryAdjustmentHeader() { }
    public InventoryAdjustmentHeader(Guid companyId, Guid branchId, Guid fiscalYearId) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty,
            "inventory_document_scope_required", "تسوية الجرد: الفرع والسنة المالية مطلوبان.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
    }
}
