namespace Ameen.Erp.Domain;

// General unit definitions only. Item-specific conversions belong to ITEM_UNIT.
public sealed class UnitOfMeasure : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string StandardCode { get; private set; } = "";
    public int QuantityScale { get; private set; }
    public string Description { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    private UnitOfMeasure() { }
    public UnitOfMeasure(Guid companyId, string code, string name, string standardCode, int quantityScale, string description,
        string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, standardCode, quantityScale, description, englishName, notes);
    public void Configure(string code, string name, string standardCode, int quantityScale, string description, string? englishName, string? notes)
    {
        Check.That(quantityScale is >= 0 and <= 6, "invalid_quantity_scale", "دقة الكمية بين صفر وست خانات.");
        Code = Check.Code(code, "رمز الوحدة"); Name = Check.Text(name, "اسم الوحدة");
        StandardCode = Check.Code(standardCode, "رمز الوحدة القياسي", 32); QuantityScale = quantityScale;
        Description = Check.Text(description, "وصف الوحدة", 500);
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000); Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "unit_revision_exhausted", "بلغ إصدار الوحدة الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}

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

public enum ItemType { Goods, Service, Material, Product }
public enum ItemTracking { None, Lot, Serial }

public sealed class Item : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public ItemType Type { get; private set; }
    public ItemTracking Tracking { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid BaseUnitId { get; private set; }
    public int? ShelfLifeDays { get; private set; }
    public decimal? ReorderLevel { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    public List<ItemUnit> Units { get; private set; } = new();
    private Item() { }
    public Item(Guid cid, string code, string name, ItemType type, ItemTracking tracking, Guid groupId, Guid baseUnitId,
        int? shelfLifeDays, decimal? reorderLevel, string? englishName, string? notes) : base(cid)
        => Configure(code, name, type, tracking, groupId, baseUnitId, shelfLifeDays, reorderLevel, englishName, notes);
    public void Configure(string code, string name, ItemType type, ItemTracking tracking, Guid groupId, Guid baseUnitId,
        int? shelfLifeDays, decimal? reorderLevel, string? englishName, string? notes)
    {
        Check.That(Enum.IsDefined(type) && Enum.IsDefined(tracking), "invalid_item_classification", "نوع الصنف أو التتبع غير صالح.");
        Check.That(shelfLifeDays is null || shelfLifeDays > 0, "invalid_shelf_life", "مدة الصلاحية موجبة عند تحديدها.");
        Check.That(shelfLifeDays is null || tracking != ItemTracking.None, "shelf_life_requires_tracking", "الصلاحية تتطلب تتبع هوية المخزون.");
        Check.That(reorderLevel is null || (reorderLevel >= 0 && reorderLevel < 100_000_000_000_000m && decimal.Round(reorderLevel.Value, 6) == reorderLevel),
            "invalid_reorder_level", "حد إعادة الطلب غير سالب وبحد أقصى ست خانات عشرية.");
        Check.That(type != ItemType.Service || (tracking == ItemTracking.None && shelfLifeDays is null && reorderLevel is null),
            "service_has_no_stock", "الخدمة بلا تتبع مخزون أو صلاحية أو حد إعادة طلب.");
        Code = Check.Code(code, "رمز الصنف"); Name = Check.Text(name, "اسم الصنف");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي"); Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        Type = type; Tracking = tracking; GroupId = groupId; BaseUnitId = baseUnitId; ShelfLifeDays = shelfLifeDays; ReorderLevel = reorderLevel; Advance();
    }
    public void SetUnits(IEnumerable<ItemUnit> units)
    {
        var list = units.ToList();
        Check.That(list.Count is >= 1 and <= 100 && list.All(x => x.ItemId == Id && x.CompanyId == CompanyId), "invalid_item_units", "وحدات الصنف من 1 إلى 100 وفي نطاق الصنف.");
        Check.That(list.Select(x => x.UnitId).Distinct().Count() == list.Count, "duplicate_item_unit", "الوحدة مكررة للصنف.");
        Check.That(list.Count(x => x.UnitId == BaseUnitId && x.FactorToBase == 1m) == 1, "base_unit_factor", "الوحدة الأساسية مطلوبة بمعامل واحد.");
        Units = list;
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance() { Check.That(Revision < int.MaxValue, "item_revision_exhausted", "بلغ إصدار الصنف الحد الأقصى.", ErrorKind.Conflict); Revision++; Touch(); }
}

public sealed class ItemUnit : CompanyEntity
{
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal FactorToBase { get; private set; }
    public string? Barcode { get; private set; }
    public decimal? SalePrice { get; private set; }
    private ItemUnit() { }
    public ItemUnit(Guid cid, Guid itemId, Guid unitId, decimal factor, string? barcode, decimal? price) : base(cid)
    {
        Check.That(factor > 0 && factor < 100_000_000_000_000m && decimal.Round(factor, 6) == factor, "invalid_conversion_factor", "معامل التحويل موجب وبحد أقصى ست خانات عشرية.");
        Check.That(price is null || (price >= 0 && price < 100_000_000_000_000m && decimal.Round(price.Value, 6) == price), "invalid_item_price", "السعر غير سالب وبحد أقصى ست خانات عشرية.");
        ItemId = itemId; UnitId = unitId; FactorToBase = factor; SalePrice = price;
        Barcode = barcode is null ? null : Check.Text(barcode, "الباركود", 128);
    }
}
