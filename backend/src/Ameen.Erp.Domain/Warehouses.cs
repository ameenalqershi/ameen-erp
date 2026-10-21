namespace Ameen.Erp.Domain;

// Initial scope inferred from INV01 isolation rule; not a complete warehouse classification policy.
public enum WarehouseType { Standard, Quarantine }
public sealed class Warehouse : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid KeeperMembershipId { get; private set; }
    public Guid InventoryAccountId { get; private set; }
    public WarehouseType Type { get; private set; }
    public bool TrackLocations { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    public List<WarehouseLocation> Locations { get; private set; } = new();
    private Warehouse() { }
    public Warehouse(Guid cid, string code, string name, Guid branchId, Guid keeperMembershipId, Guid inventoryAccountId,
        WarehouseType type, bool trackLocations, string? englishName, string? notes) : base(cid)
        => Configure(code, name, branchId, keeperMembershipId, inventoryAccountId, type, trackLocations, englishName, notes);
    public void Configure(string code, string name, Guid branchId, Guid keeperMembershipId, Guid inventoryAccountId,
        WarehouseType type, bool trackLocations, string? englishName, string? notes)
    {
        Check.That(Enum.IsDefined(type), "invalid_warehouse_type", "نوع المخزن غير معروف.");
        Check.That(branchId != Guid.Empty && keeperMembershipId != Guid.Empty && inventoryAccountId != Guid.Empty, "warehouse_references_required", "الفرع والأمين والحساب مطلوبة.");
        Code = Check.Code(code, "رمز المخزن"); Name = Check.Text(name, "اسم المخزن");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        BranchId = branchId; KeeperMembershipId = keeperMembershipId; InventoryAccountId = inventoryAccountId;
        Type = type; TrackLocations = trackLocations; Advance();
    }
    public void SetLocations(List<WarehouseLocation> locations)
    {
        Check.That(locations.Count <= 500 && (TrackLocations ? locations.Count > 0 : locations.Count == 0), "invalid_warehouse_locations", "تتبع الرف يتطلب مواقع؛ دون التتبع لا تقبل مواقع، والحد 500.");
        Check.That(locations.All(x => x.CompanyId == CompanyId && x.WarehouseId == Id), "warehouse_location_owner", "الموقع خارج المخزن.");
        Check.That(locations.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() == locations.Count, "duplicate_location", "رمز الموقع مكرر في المخزن.");
        Locations = locations;
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "warehouse_revision_exhausted", "بلغ إصدار المخزن الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
public sealed class WarehouseLocation : CompanyEntity
{
    public Guid WarehouseId { get; private set; }
    public string Code { get; private set; } = "";
    public string Description { get; private set; } = "";
    private WarehouseLocation() { }
    public WarehouseLocation(Guid cid, Guid warehouseId, string code, string description) : base(cid)
    { WarehouseId = warehouseId; Code = Check.Code(code, "رمز الموقع"); Description = Check.Text(description, "وصف الموقع", 500); }
}
