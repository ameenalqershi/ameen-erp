namespace Ameen.Erp.Domain;

// INV14-FG01 only. Remaining document fields and operational transitions are absent.
public sealed class InventoryDamageHeader : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    private InventoryDamageHeader() { }
    public InventoryDamageHeader(Guid companyId, Guid branchId, Guid fiscalYearId) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty,
            "inventory_document_scope_required", "التالف والعزل: الفرع والسنة المالية مطلوبان.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
    }
}
