namespace Ameen.Erp.Domain;

// SAL15 structural customer-group definition. No pricing/credit override,
// historical invoice mutation or operational persistence is enabled.
public sealed class CustomerGroup : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public Guid? ParentGroupId { get; private set; }
    public Guid PriceListId { get; private set; }
    public decimal DefaultCreditLimit { get; private set; }
    public Guid CreditCurrencyId { get; private set; }
    public Guid PaymentTermId { get; private set; }
    public Guid ControlAccountId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CustomerGroup() { }

    public CustomerGroup(Guid companyId, string code, string name, Guid priceListId,
        decimal defaultCreditLimit, Guid creditCurrencyId, Guid paymentTermId,
        Guid controlAccountId, string createdBy, DateTimeOffset createdAt,
        string? englishName = null, string? notes = null, Guid? parentGroupId = null)
        : base(companyId)
    {
        Check.That(priceListId != Guid.Empty && creditCurrencyId != Guid.Empty &&
            paymentTermId != Guid.Empty && controlAccountId != Guid.Empty &&
            parentGroupId != Guid.Empty, "customer_group_reference_required",
            "قائمة الأسعار والعملة وشروط الدفع وحساب الرقابة مراجع إلزامية.");
        Check.That(defaultCreditLimit >= 0m && defaultCreditLimit < 100_000_000_000_000m &&
            decimal.Round(defaultCreditLimit, 6) == defaultCreditLimit,
            "invalid_customer_group_credit_limit", "حد الائتمان غير سالب ودقيق حتى ست خانات عشرية.");
        Check.That(createdAt != default, "customer_group_audit_date_required", "وقت الإنشاء مطلوب.");
        Code = Check.Code(code, "رمز مجموعة العملاء");
        Name = Check.Text(name, "اسم مجموعة العملاء");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        ParentGroupId = parentGroupId;
        PriceListId = priceListId;
        DefaultCreditLimit = defaultCreditLimit;
        CreditCurrencyId = creditCurrencyId;
        PaymentTermId = paymentTermId;
        ControlAccountId = controlAccountId;
        CreatedBy = Check.Text(createdBy, "منشئ المجموعة", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
