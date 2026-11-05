namespace Ameen.Erp.Domain;

// INV10-FG01 only. Remaining document fields and operational transitions are absent.
public sealed class InventoryTransferReceiptHeader : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    private InventoryTransferReceiptHeader() { }
    public InventoryTransferReceiptHeader(Guid companyId, Guid branchId, Guid fiscalYearId) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty,
            "inventory_document_scope_required", "استلام التحويل: الفرع والسنة المالية مطلوبان.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
    }
}
