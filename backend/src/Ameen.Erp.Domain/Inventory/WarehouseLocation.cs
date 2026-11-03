namespace Ameen.Erp.Domain;

public sealed class WarehouseLocation : CompanyEntity
{
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = "";
    public string Description { get; private set; } = "";
    private WarehouseLocation() { }
    public WarehouseLocation(Guid cid, Guid warehouseId, string code, string description) : base(cid)
    { WarehouseId = warehouseId; Code = Check.Code(code, "رمز الموقع"); Description = Check.Text(description, "وصف الموقع", 500); }
    // The code is the location identity within a warehouse; descriptions are editable
    // until a recorded movement freezes the warehouse's location structure.
    public void SetDescription(string description)
    {
        Description = Check.Text(description, "وصف الموقع", 500);
        Touch();
    }
}
