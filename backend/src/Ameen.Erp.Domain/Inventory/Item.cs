namespace Ameen.Erp.Domain;

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
