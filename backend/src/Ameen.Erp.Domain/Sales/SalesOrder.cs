namespace Ameen.Erp.Domain;

// SAL04 structural order draft only. No reservation, execution tracking,
// document transitions, taxation, posting or quote conversion is enabled.
public sealed class SalesOrder : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    public string DocumentNumber { get; private set; } = "";
    public DateOnly DocumentDate { get; private set; }
    public DateOnly EffectDate { get; private set; }
    public Guid CurrencyId { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public string Description { get; private set; } = "";
    public string? ExternalReference { get; private set; }
    public string Status { get; private set; } = "";
    public Guid? SourceQuotationId { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateOnly DeliveryDate { get; private set; }
    public string PaymentTerms { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public IReadOnlyList<SalesOrderLine> Lines { get; private set; } =
        Array.Empty<SalesOrderLine>();

    private SalesOrder() { }

    public SalesOrder(Guid companyId, Guid branchId, Guid fiscalYearId,
        string documentNumber, DateOnly documentDate, DateOnly effectDate,
        Guid currencyId, decimal exchangeRate, string description,
        string? externalReference, string status, Guid? sourceQuotationId,
        Guid customerId, DateOnly deliveryDate, string paymentTerms,
        IEnumerable<(Guid ItemId, Guid UnitId, decimal Quantity,
            decimal Price, decimal? Discount, Guid? TaxCodeId)> lines,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty &&
            currencyId != Guid.Empty && customerId != Guid.Empty &&
            sourceQuotationId != Guid.Empty,
            "sal04_reference_required", "مرجع الفرع أو السنة أو العملة أو العميل غير صالح.");
        Check.That(documentDate != default && effectDate != default &&
            deliveryDate != default,
            "sal04_dates_required", "التواريخ المطلوبة غير صالحة.");
        Check.That(exchangeRate > 0m && exchangeRate < 100_000_000_000_000m,
            "sal04_exchange_rate_invalid", "سعر الصرف غير صالح.");
        Check.That(createdAt != default, "sal04_audit_date_required", "وقت الإنشاء مطلوب.");
        var number = Check.Text(documentNumber, "رقم أمر البيع", 64);
        var memo = Check.Text(description, "بيان الأمر", 2000);
        var reference = externalReference is null ? null :
            Check.Text(externalReference, "المرجع الخارجي", 200);
        var statusCode = Check.Text(status, "حالة الأمر", 64);
        var terms = Check.Text(paymentTerms, "شروط الدفع", 2000);
        var actor = Check.Text(createdBy, "منشئ الأمر");
        Check.That(lines is not null, "sal04_lines_required", "سطور أمر البيع مطلوبة.");
        var snapshot = lines!.ToArray();
        Check.That(snapshot.Length > 0, "sal04_lines_required", "يجب وجود سطر بيع.");
        var mapped = new List<SalesOrderLine>(snapshot.Length);
        foreach (var line in snapshot)
            mapped.Add(new SalesOrderLine(companyId, Id, mapped.Count + 1,
                line.ItemId, line.UnitId, line.Quantity, line.Price,
                line.Discount, line.TaxCodeId));

        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        DocumentNumber = number;
        DocumentDate = documentDate;
        EffectDate = effectDate;
        CurrencyId = currencyId;
        ExchangeRate = exchangeRate;
        Description = memo;
        ExternalReference = reference;
        Status = statusCode;
        SourceQuotationId = sourceQuotationId;
        CustomerId = customerId;
        DeliveryDate = deliveryDate;
        PaymentTerms = terms;
        CreatedBy = actor;
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
        Lines = mapped.AsReadOnly();
    }
}

public sealed class SalesOrderLine : CompanyEntity
{
    public Guid OrderId { get; private set; }
    public int Position { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Price { get; private set; }
    public decimal? Discount { get; private set; }
    public Guid? TaxCodeId { get; private set; }

    private SalesOrderLine() { }

    internal SalesOrderLine(Guid companyId, Guid orderId, int position,
        Guid itemId, Guid unitId, decimal quantity, decimal price,
        decimal? discount, Guid? taxCodeId) : base(companyId)
    {
        Check.That(orderId != Guid.Empty && position > 0 &&
            itemId != Guid.Empty && unitId != Guid.Empty && taxCodeId != Guid.Empty,
            "sal04_line_reference_required", "مراجع السطر غير صالحة.");
        Check.Amount(quantity, positive: true);
        Check.Amount(price);
        if (discount.HasValue) Check.Amount(discount.Value);
        OrderId = orderId;
        Position = position;
        ItemId = itemId;
        UnitId = unitId;
        Quantity = quantity;
        Price = price;
        Discount = discount;
        TaxCodeId = taxCodeId;
    }
}
