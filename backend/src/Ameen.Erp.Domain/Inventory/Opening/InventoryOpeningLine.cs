namespace Ameen.Erp.Domain;
public sealed class InventoryOpeningLine : CompanyEntity
{
    public Guid OpeningId { get; private set; }
    public int Position { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid ItemVersion { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid? IdentityId { get; private set; }
    public Guid? IdentityVersion { get; private set; }
    public string ItemCode { get; private set; } = "";
    public string UnitCode { get; private set; } = "";
    public string? LotNumber { get; private set; }
    public string? SerialNumber { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal FactorToBase { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal Value { get; private set; }
    private InventoryOpeningLine() { }
    public InventoryOpeningLine(Guid cid, Guid openingId, int position, Item item, UnitOfMeasure unit,
        InventoryIdentity? identity, decimal quantity, decimal factor, decimal baseQuantity, decimal cost, int currencyScale) : base(cid)
    {
        Check.That(item.CompanyId == cid && unit.CompanyId == cid && (identity is null || identity.CompanyId == cid && identity.ItemId == item.Id), "opening_line_scope", "مراجع السطر خارج النطاق.");
        Check.That(quantity > 0 && quantity < 100_000_000_000_000m && decimal.Round(quantity, 6) == quantity,
            "invalid_opening_quantity", "الكمية موجبة وبحد أقصى ست خانات عشرية.");
        Check.That(cost >= 0 && cost < 100_000_000_000_000m && decimal.Round(cost, 6) == cost,
            "invalid_opening_cost", "التكلفة غير سالبة وبحد أقصى ست خانات عشرية.");
        Check.That(factor > 0 && factor < 100_000_000_000_000m && decimal.Round(factor, 6) == factor &&
            baseQuantity > 0 && baseQuantity < 100_000_000_000_000m && decimal.Round(baseQuantity, 6) == baseQuantity,
            "invalid_opening_conversion", "تحويل الكمية غير صالح.");
        try { Check.That(checked(quantity * factor) == baseQuantity, "invalid_opening_conversion", "الكمية الأساسية لا تطابق التحويل."); }
        catch (OverflowException) { throw new BusinessException("invalid_opening_conversion", "تحويل الكمية يتجاوز الحد."); }
        decimal value;
        try { value = decimal.Round(checked(quantity * cost), currencyScale, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { throw new BusinessException("opening_value_overflow", "قيمة السطر تتجاوز الحد."); }
        Check.That(value < 100_000_000_000_000m, "opening_value_overflow", "قيمة السطر تتجاوز الحد.");
        OpeningId = openingId; Position = position; ItemId = item.Id; ItemVersion = item.Version; UnitId = unit.Id;
        IdentityId = identity?.Id; IdentityVersion = identity?.Version; ItemCode = item.Code; UnitCode = unit.Code;
        LotNumber = identity?.LotNumber; SerialNumber = identity?.SerialNumber;
        Quantity = quantity; FactorToBase = factor; BaseQuantity = baseQuantity; UnitCost = cost; Value = value;
    }
    public InventoryOpeningLine CopyFor(Guid openingId) => new()
    {
        CompanyId = CompanyId, OpeningId = openingId, Position = Position, ItemId = ItemId, ItemVersion = ItemVersion,
        UnitId = UnitId, IdentityId = IdentityId, IdentityVersion = IdentityVersion, ItemCode = ItemCode, UnitCode = UnitCode,
        LotNumber = LotNumber, SerialNumber = SerialNumber, Quantity = Quantity, FactorToBase = FactorToBase,
        BaseQuantity = BaseQuantity, UnitCost = UnitCost, Value = Value
    };
}
