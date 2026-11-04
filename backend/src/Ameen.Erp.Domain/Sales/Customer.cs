namespace Ameen.Erp.Domain;

// SAL01-01: structural card only. An authorized customer lifecycle, identity
// requirements by country, credit-limit overrides and SAL15 groups are pending.
// No operational API is available and EF writes are deliberately denied.
public sealed class Customer : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public string LegalName { get; private set; } = "";
    public Guid CustomerCategoryId { get; private set; }
    public Guid ControlAccountId { get; private set; }
    public Guid TransactionCurrencyId { get; private set; }
    public decimal CreditLimit { get; private set; }
    public int CreditDays { get; private set; }
    public Guid? SalespersonId { get; private set; }
    public string? Phone { get; private set; }
    public Guid CountryId { get; private set; }
    public string? IdentityType { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? City { get; private set; }
    public string? District { get; private set; }
    public string? Street { get; private set; }
    public string? BuildingNumber { get; private set; }
    public string? PostalCode { get; private set; }
    public string? AdditionalNumber { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private Customer() { }

    public Customer(Guid companyId, string code, string name, string legalName,
        Guid customerCategoryId, Guid controlAccountId, Guid transactionCurrencyId,
        decimal creditLimit, int creditDays, Guid countryId, string createdBy,
        DateTimeOffset createdAt, string? englishName = null, string? notes = null,
        Guid? salespersonId = null, string? phone = null,
        string? identityType = null, string? taxNumber = null, string? city = null,
        string? district = null, string? street = null, string? buildingNumber = null,
        string? postalCode = null, string? additionalNumber = null) : base(companyId)
    {
        Check.That(customerCategoryId != Guid.Empty && controlAccountId != Guid.Empty &&
            transactionCurrencyId != Guid.Empty && countryId != Guid.Empty &&
            salespersonId != Guid.Empty, "customer_reference_required",
            "تصنيف العميل وحساب الرقابة والعملة والدولة مراجع إلزامية.");
        Check.That(creditLimit >= 0m && creditLimit < 100_000_000_000_000m &&
            decimal.Round(creditLimit, 6) == creditLimit, "invalid_customer_credit_limit",
            "الحد الائتماني غير سالب ودقيق حتى ست خانات عشرية.");
        Check.That(creditDays >= 0, "invalid_customer_credit_days",
            "أيام الائتمان لا يمكن أن تكون سالبة.");
        Check.That(createdAt != default, "customer_audit_date_required", "وقت الإنشاء مطلوب.");
        Code = Check.Code(code, "رمز العميل");
        Name = Check.Text(name, "اسم العميل");
        LegalName = Check.Text(legalName, "الاسم القانوني");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        Phone = phone is null ? null : Check.Text(phone, "الهاتف", 100);
        IdentityType = identityType is null ? null : Check.Text(identityType, "نوع المعرف", 32);
        TaxNumber = taxNumber is null ? null : Check.Text(taxNumber, "الرقم الضريبي", 64);
        City = city is null ? null : Check.Text(city, "المدينة", 200);
        District = district is null ? null : Check.Text(district, "الحي", 200);
        Street = street is null ? null : Check.Text(street, "الشارع", 200);
        BuildingNumber = buildingNumber is null ? null : Check.Text(buildingNumber, "رقم المبنى", 32);
        PostalCode = postalCode is null ? null : Check.Text(postalCode, "الرمز البريدي", 32);
        AdditionalNumber = additionalNumber is null ? null : Check.Text(additionalNumber, "الرقم الإضافي", 32);
        CustomerCategoryId = customerCategoryId;
        ControlAccountId = controlAccountId;
        TransactionCurrencyId = transactionCurrencyId;
        CreditLimit = creditLimit;
        CreditDays = creditDays;
        SalespersonId = salespersonId;
        CountryId = countryId;
        CreatedBy = Check.Text(createdBy, "منشئ بطاقة العميل", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
