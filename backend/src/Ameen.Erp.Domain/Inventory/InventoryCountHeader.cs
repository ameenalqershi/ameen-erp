namespace Ameen.Erp.Domain;

// INV12-FG01 only. StatusCode is opaque; no lifecycle values are assumed.
public sealed class InventoryCountHeader : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public Guid WarehouseId { get; private set; }
    private InventoryCountHeader() { }
    public InventoryCountHeader(Guid companyId, Guid? branchId, string statusCode, Guid warehouseId)
        : base(companyId)
    {
        Check.That(branchId is null || branchId != Guid.Empty,
            "inventory_document_branch_invalid", "فرع أمر الجرد غير صالح.");
        Check.That(warehouseId != Guid.Empty, "inventory_count_warehouse_required",
            "مخزن أمر الجرد مطلوب.");
        BranchId = branchId;
        StatusCode = Check.Code(statusCode, "حالة أمر الجرد", 32);
        WarehouseId = warehouseId;
    }
}
