namespace Ameen.Erp.Domain;

// PUR01: structural supplier identity and terms only. Bank-detail changes require
// a different reviewer; country-specific tax/IBAN decisions and PUR03/PUR11
// postings are not approved. No mutation workflow or financial effect is enabled.
public sealed class Supplier : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public bool IsActive { get; private set; }
    public string? Notes { get; private set; }
    public string LegalName { get; private set; } = "";
    public Guid ControlAccountId { get; private set; }
    public Guid TransactionCurrencyId { get; private set; }
    public string PaymentTerms { get; private set; } = "";
    public int DueDays { get; private set; }
    public string? TaxNumber { get; private set; }
    public Guid CountryId { get; private set; }
    public string? BankBeneficiary { get; private set; }
    public string? Iban { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }
    public string City { get; private set; } = "";
    public string? District { get; private set; }
    public string Street { get; private set; } = "";
    public string? BuildingNumber { get; private set; }
    public string? PostalCode { get; private set; }
    public string? AdditionalNumber { get; private set; }

    private Supplier() { }

    public Supplier(Guid companyId, string code, string name, string legalName,
        Guid controlAccountId, Guid transactionCurrencyId, string paymentTerms,
        int dueDays, Guid countryId, string city, string street, bool isActive,
        string createdBy, DateTimeOffset createdAt, string? englishName = null,
        string? notes = null, string? taxNumber = null,
        string? bankBeneficiary = null, string? iban = null,
        string? district = null, string? buildingNumber = null,
        string? postalCode = null, string? additionalNumber = null) : base(companyId)
    {
        Check.That(controlAccountId != Guid.Empty &&
            transactionCurrencyId != Guid.Empty && countryId != Guid.Empty,
            "supplier_reference_required",
            "حساب الرقابة وعملة التعامل والدولة مراجع مطلوبة.");
        Check.That(dueDays >= 0, "supplier_due_days_invalid",
            "أيام الاستحقاق لا تكون سالبة.");
        Check.That(createdAt != default, "supplier_audit_date_required",
            "وقت إنشاء بطاقة المورد مطلوب.");

        Code = Check.Code(code, "رمز المورد");
        Name = Check.Text(name, "اسم المورد");
        LegalName = Check.Text(legalName, "الاسم القانوني");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        PaymentTerms = Check.Text(paymentTerms, "شروط السداد", 2000);
        TaxNumber = taxNumber is null ? null : Check.Text(taxNumber, "الرقم الضريبي", 64);
        BankBeneficiary = bankBeneficiary is null ? null : Check.Text(bankBeneficiary, "المستفيد البنكي", 200);
        // No country-specific IBAN length/checksum rule has been approved yet.
        Iban = iban is null ? null : Check.Text(iban, "IBAN", 64);
        City = Check.Text(city, "المدينة");
        District = district is null ? null : Check.Text(district, "الحي");
        Street = Check.Text(street, "الشارع");
        BuildingNumber = buildingNumber is null ? null : Check.Text(buildingNumber, "رقم المبنى", 32);
        PostalCode = postalCode is null ? null : Check.Text(postalCode, "الرمز البريدي", 32);
        AdditionalNumber = additionalNumber is null ? null : Check.Text(additionalNumber, "الرقم الإضافي", 32);

        ControlAccountId = controlAccountId;
        TransactionCurrencyId = transactionCurrencyId;
        DueDays = dueDays;
        CountryId = countryId;
        IsActive = isActive;
        CreatedBy = Check.Text(createdBy, "منشئ بطاقة المورد");
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
