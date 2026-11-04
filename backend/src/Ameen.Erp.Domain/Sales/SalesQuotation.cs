namespace Ameen.Erp.Domain;

// SAL14 structural quotation only. No approval, reservation, posting or
// conversion-to-order logic exists until SAL04/SAL02 and authorization gates pass.
public sealed class SalesQuotation : CompanyEntity
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
    public string CustomerOrProspect { get; private set; } = "";
    public Guid? CustomerId { get; private set; }
    public DateOnly ExpiresOn { get; private set; }
    public string DeliveryTerms { get; private set; } = "";
    public string PaymentTerms { get; private set; } = "";
    public string QuotationIssue { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public IReadOnlyList<SalesQuotationLine> Lines { get; private set; } =
        Array.Empty<SalesQuotationLine>();

    private SalesQuotation() { }

    public SalesQuotation(Guid companyId, Guid branchId, Guid fiscalYearId,
        string documentNumber, DateOnly documentDate, DateOnly effectDate,
        Guid currencyId, decimal exchangeRate, string description,
        string? externalReference, string status, string customerOrProspect,
        Guid? customerId, DateOnly expiresOn, string deliveryTerms,
        string paymentTerms, string quotationIssue,
        IEnumerable<(Guid ItemOrServiceId, Guid UnitId, decimal Quantity,
            decimal Price, decimal? Discount, Guid? TaxCodeId)> lines,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty &&
            currencyId != Guid.Empty && customerId != Guid.Empty,
            "sal14_reference_required", "مراجع الفرع والسنة والعملة غير صالحة.");
        Check.That(documentDate != default && effectDate != default &&
            expiresOn != default,
            "sal14_dates_required", "تواريخ العرض مطلوبة.");
        // FX conversion and price policy require an approved currency contract:
        // only reject non-positive/unrepresentable numbers here.
        Check.That(exchangeRate > 0m && exchangeRate < 100_000_000_000_000m,
            "sal14_exchange_rate_invalid", "سعر الصرف غير صالح.");
        Check.That(createdAt != default, "sal14_audit_date_required",
            "وقت الإنشاء مطلوب.");
        var validatedNumber = Check.Text(documentNumber, "رقم العرض", 64);
        var validatedDescription = Check.Text(description, "البيان", 2000);
        var validatedReference = externalReference is null ? null :
            Check.Text(externalReference, "المرجع الخارجي", 200);
        var validatedStatus = Check.Text(status, "الحالة", 64);
        var validatedCustomer = Check.Text(customerOrProspect, "العميل أو المحتمل");
        var validatedDelivery = Check.Text(deliveryTerms, "شروط التسليم", 2000);
        var validatedPayment = Check.Text(paymentTerms, "شروط الدفع", 2000);
        var validatedIssue = Check.Text(quotationIssue, "إصدار العرض", 64);
        var validatedActor = Check.Text(createdBy, "منشئ العرض", 200);
        Check.That(lines is not null, "sal14_lines_required", "سطور العرض مطلوبة.");
        var inputs = lines!.ToArray();
        Check.That(inputs.Length > 0, "sal14_lines_required", "سطور العرض مطلوبة.");
        var mapped = new List<SalesQuotationLine>(inputs.Length);
        foreach (var line in inputs)
        {
            mapped.Add(new SalesQuotationLine(companyId, Id, mapped.Count + 1,
                line.ItemOrServiceId, line.UnitId, line.Quantity,
                line.Price, line.Discount, line.TaxCodeId));
        }
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        DocumentNumber = validatedNumber;
        DocumentDate = documentDate;
        EffectDate = effectDate;
        CurrencyId = currencyId;
        ExchangeRate = exchangeRate;
        Description = validatedDescription;
        ExternalReference = validatedReference;
        Status = validatedStatus;
        CustomerOrProspect = validatedCustomer;
        CustomerId = customerId;
        ExpiresOn = expiresOn;
        DeliveryTerms = validatedDelivery;
        PaymentTerms = validatedPayment;
        QuotationIssue = validatedIssue;
        CreatedBy = validatedActor;
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
        Lines = mapped.AsReadOnly();
    }
}

public sealed class SalesQuotationLine : CompanyEntity
{
    public Guid QuotationId { get; private set; }
    public int Position { get; private set; }
    public Guid ItemOrServiceId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Price { get; private set; }
    public decimal? Discount { get; private set; }
    public Guid? TaxCodeId { get; private set; }

    private SalesQuotationLine() { }

    internal SalesQuotationLine(Guid companyId, Guid quotationId, int position,
        Guid itemOrServiceId, Guid unitId, decimal quantity, decimal price,
        decimal? discount, Guid? taxCodeId) : base(companyId)
    {
        Check.That(quotationId != Guid.Empty && position > 0 &&
            itemOrServiceId != Guid.Empty && unitId != Guid.Empty &&
            taxCodeId != Guid.Empty,
            "sal14_line_reference_required", "مرجع صنف أو وحدة أو ضريبة غير صالح.");
        Check.Amount(quantity, positive: true);
        Check.Amount(price);
        if (discount.HasValue) Check.Amount(discount.Value);
        QuotationId = quotationId;
        Position = position;
        ItemOrServiceId = itemOrServiceId;
        UnitId = unitId;
        Quantity = quantity;
        Price = price;
        Discount = discount;
        TaxCodeId = taxCodeId;
    }
}
