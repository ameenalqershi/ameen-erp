namespace Ameen.Erp.Domain;

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
