namespace Ameen.Erp.Domain;

// SAL02 structural price-list contract only. A stored status is opaque until the
// original activation/priority/overlap and discount authorization rules are approved.
// No EF registration, write endpoint, pricing resolution or sale mutation is enabled.
public sealed class SalesPriceList : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string Status { get; private set; } = "";
    public string Name { get; private set; } = "";
    public Guid CurrencyId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IncludesTax { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public IReadOnlyList<SalesPriceListLine> Lines { get; private set; } =
        Array.Empty<SalesPriceListLine>();

    private SalesPriceList() { }

    public SalesPriceList(Guid companyId, Guid? branchId, string status, string name,
        Guid currencyId, DateOnly effectiveFrom, DateOnly? effectiveTo, bool includesTax,
        IEnumerable<(Guid ItemId, Guid UnitId, decimal Price,
            decimal DiscountLimit, decimal? QuantityThreshold)> lines,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && currencyId != Guid.Empty,
            "sal02_reference_required", "مرجع الفرع أو العملة غير صالح.");
        Check.That(effectiveFrom != default && effectiveTo != default(DateOnly) &&
            (!effectiveTo.HasValue || effectiveTo.Value >= effectiveFrom),
            "sal02_effective_dates_invalid", "تواريخ صلاحية القائمة غير صالحة.");
        Check.That(createdAt != default, "sal02_audit_date_required", "وقت الإنشاء مطلوب.");

        var validatedStatus = Check.Text(status, "حالة قائمة الأسعار", 64);
        var validatedName = Check.Text(name, "اسم قائمة الأسعار");
        var validatedActor = Check.Text(createdBy, "منشئ قائمة الأسعار");
        Check.That(lines is not null, "sal02_lines_required", "سطور الأسعار مطلوبة.");
        var inputs = lines!.ToArray();
        Check.That(inputs.Length > 0, "sal02_lines_required", "يجب وجود سطر سعر.");
        Check.That(inputs.Length < int.MaxValue, "sal02_lines_invalid", "عدد السطور غير صالح.");

        var validatedLines = new List<SalesPriceListLine>(inputs.Length);
        foreach (var line in inputs)
        {
            validatedLines.Add(new SalesPriceListLine(companyId, Id, validatedLines.Count + 1,
                line.ItemId, line.UnitId, line.Price, line.DiscountLimit,
                line.QuantityThreshold));
        }
        BranchId = branchId;
        Status = validatedStatus;
        Name = validatedName;
        CurrencyId = currencyId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IncludesTax = includesTax;
        CreatedBy = validatedActor;
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
        Lines = validatedLines.AsReadOnly();
    }
}

public sealed class SalesPriceListLine : CompanyEntity
{
    public Guid PriceListId { get; private set; }
    public int Position { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Price { get; private set; }
    public decimal DiscountLimit { get; private set; }
    public decimal? QuantityThreshold { get; private set; }

    private SalesPriceListLine() { }

    internal SalesPriceListLine(Guid companyId, Guid priceListId, int position,
        Guid itemId, Guid unitId, decimal price, decimal discountLimit,
        decimal? quantityThreshold) : base(companyId)
    {
        Check.That(priceListId != Guid.Empty && itemId != Guid.Empty &&
            unitId != Guid.Empty && position > 0,
            "sal02_line_reference_required", "مرجع القائمة أو الصنف أو الوحدة غير صالح.");
        Check.Amount(price);
        Check.Amount(discountLimit);
        if (quantityThreshold.HasValue) Check.Amount(quantityThreshold.Value);
        PriceListId = priceListId;
        Position = position;
        ItemId = itemId;
        UnitId = unitId;
        Price = price;
        DiscountLimit = discountLimit;
        QuantityThreshold = quantityThreshold;
    }
}
