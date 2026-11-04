namespace Ameen.Erp.Domain;

// SAL05 delivery document structural contract only. Inventory issue recognition,
// source-order line allocation, reservation and invoicing policies remain unapproved.
public sealed class SalesDeliveryOrder : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    public string DocumentNumber { get; private set; } = "";
    public DateOnly DocumentDate { get; private set; }
    public DateOnly EffectDate { get; private set; }
    public Guid? CurrencyId { get; private set; }
    public decimal? ExchangeRate { get; private set; }
    public string Description { get; private set; } = "";
    public string? ExternalReference { get; private set; }
    public string Status { get; private set; } = "";
    public Guid CustomerId { get; private set; }
    public Guid? SalesOrderId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string Recipient { get; private set; } = "";
    public string? ShippingReference { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public IReadOnlyList<SalesDeliveryOrderLine> Lines { get; private set; } =
        Array.Empty<SalesDeliveryOrderLine>();

    private SalesDeliveryOrder() { }

    public SalesDeliveryOrder(Guid companyId, Guid branchId, Guid fiscalYearId,
        string documentNumber, DateOnly documentDate, DateOnly effectDate,
        Guid? currencyId, decimal? exchangeRate, string description,
        string? externalReference, string status, Guid customerId,
        Guid? salesOrderId, Guid warehouseId, string recipient,
        string? shippingReference,
        IEnumerable<(Guid ItemId, Guid UnitId, decimal Quantity,
            string? LotOrSerial)> lines,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty &&
            customerId != Guid.Empty && warehouseId != Guid.Empty &&
            salesOrderId != Guid.Empty && currencyId != Guid.Empty,
            "sal05_reference_required", "مراجع التسليم مطلوبة وصحيحة.");
        Check.That(documentDate != default && effectDate != default,
            "sal05_dates_required", "تاريخ الوثيقة والأثر مطلوبان.");
        if (exchangeRate.HasValue)
            Check.That(exchangeRate.Value > 0m &&
                exchangeRate.Value < 100_000_000_000_000m,
                "sal05_exchange_rate_invalid", "سعر الصرف غير صالح.");
        Check.That(createdAt != default, "sal05_audit_date_required",
            "وقت الإنشاء مطلوب.");

        var number = Check.Text(documentNumber, "رقم تسليم المبيعات", 64);
        var memo = Check.Text(description, "بيان التسليم", 2000);
        var reference = externalReference is null ? null :
            Check.Text(externalReference, "المرجع الخارجي", 200);
        var state = Check.Text(status, "حالة التسليم", 64);
        var receiver = Check.Text(recipient, "المستلم");
        var shipping = shippingReference is null ? null :
            Check.Text(shippingReference, "مرجع الشحن", 200);
        var actor = Check.Text(createdBy, "منشئ التسليم");
        Check.That(lines is not null, "sal05_lines_required",
            "سطور التسليم مطلوبة.");
        var snapshot = lines!.ToArray();
        Check.That(snapshot.Length > 0, "sal05_lines_required",
            "يجب أن يحتوي التسليم على صنف واحد على الأقل.");
        var lineValues = new List<SalesDeliveryOrderLine>(snapshot.Length);
        foreach (var line in snapshot)
            lineValues.Add(new SalesDeliveryOrderLine(companyId, Id, lineValues.Count + 1,
                line.ItemId, line.UnitId, line.Quantity, line.LotOrSerial));

        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        DocumentNumber = number;
        DocumentDate = documentDate;
        EffectDate = effectDate;
        CurrencyId = currencyId;
        ExchangeRate = exchangeRate;
        Description = memo;
        ExternalReference = reference;
        Status = state;
        CustomerId = customerId;
        SalesOrderId = salesOrderId;
        WarehouseId = warehouseId;
        Recipient = receiver;
        ShippingReference = shipping;
        CreatedBy = actor;
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
        Lines = lineValues.AsReadOnly();
    }
}

public sealed class SalesDeliveryOrderLine : CompanyEntity
{
    public Guid DeliveryId { get; private set; }
    public int Position { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid UnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public string? LotOrSerial { get; private set; }

    private SalesDeliveryOrderLine() { }

    internal SalesDeliveryOrderLine(Guid companyId, Guid deliveryId, int position,
        Guid itemId, Guid unitId, decimal quantity, string? lotOrSerial) : base(companyId)
    {
        Check.That(deliveryId != Guid.Empty && position > 0 &&
            itemId != Guid.Empty && unitId != Guid.Empty,
            "sal05_line_reference_required", "مرجع صنف أو وحدة التسليم غير صالح.");
        Check.Amount(quantity, positive: true);
        DeliveryId = deliveryId;
        Position = position;
        ItemId = itemId;
        UnitId = unitId;
        Quantity = quantity;
        LotOrSerial = lotOrSerial is null ? null :
            Check.Text(lotOrSerial, "رقم الدفعة أو التسلسل", 200);
    }
}
