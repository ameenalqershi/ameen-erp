namespace Ameen.Erp.Domain;

public sealed class ItemGroup : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid SalesAccountId { get; private set; }
    public Guid? InventoryAccountId { get; private set; }
    public Guid? CostAccountId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    private ItemGroup() { }
    public ItemGroup(Guid cid, string code, string name, Guid salesAccountId, Guid? parentId = null,
        Guid? inventoryAccountId = null, Guid? costAccountId = null, string? englishName = null, string? notes = null) : base(cid)
        => Configure(code, name, salesAccountId, parentId, inventoryAccountId, costAccountId, englishName, notes);
    public void Configure(string code, string name, Guid salesAccountId, Guid? parentId, Guid? inventoryAccountId,
        Guid? costAccountId, string? englishName, string? notes)
    {
        Code = Check.Code(code, "رمز المجموعة"); Name = Check.Text(name, "اسم المجموعة");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        Check.That(salesAccountId != Guid.Empty, "sales_account_required", "حساب المبيعات مطلوب.");
        SalesAccountId = salesAccountId; ParentId = parentId; InventoryAccountId = inventoryAccountId; CostAccountId = costAccountId; Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "item_group_revision_exhausted", "بلغ إصدار المجموعة الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
