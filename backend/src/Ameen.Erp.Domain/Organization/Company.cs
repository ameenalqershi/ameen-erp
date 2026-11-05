namespace Ameen.Erp.Domain;

public sealed class Company : Entity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string CountryCode { get; private set; } = "";
    public string FunctionalCurrency { get; private set; } = "";
    public int CurrencyScale { get; private set; }
    public bool RequireSeparateApprover { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public string? LegalName { get; private set; }
    public string? Jurisdiction { get; private set; }
    public DateOnly? FiscalYearStartsOn { get; private set; }
    public string? CommercialRegistration { get; private set; }
    public string? TaxRegistration { get; private set; }
    public string? City { get; private set; }
    public string? District { get; private set; }
    public string? Street { get; private set; }
    public string? BuildingNumber { get; private set; }
    public string? PostalCode { get; private set; }
    public string? AdditionalNumber { get; private set; }
    public bool IsProfileComplete => LegalName is not null && Jurisdiction is not null && FiscalYearStartsOn is not null && City is not null && Street is not null;
    private Company() { }
    public Company(Guid id, string code, string name, string country, string currency, int scale, CompanyProfile profile)
    {
        Check.That(id != Guid.Empty, "company_required", "معرف الشركة مطلوب."); Id = id;
        Configure(code, name, country, currency, scale, profile);
    }
    public void Configure(string code, string name, string country, string currency, int scale, CompanyProfile profile)
    {
        Check.That(profile is not null, "company_profile_required", "بيانات الشركة القانونية والعنوان مطلوبة.");
        Check.That(scale is >= 0 and <= 6, "invalid_scale", "دقة العملة بين صفر وست خانات.");
        Code = Check.Code(code, "رمز الشركة"); Name = Check.Text(name, "اسم الشركة");
        CountryCode = Check.Code(country, "الدولة", 2);
        Check.That(Iso3166CountryCatalog.IsAssignedAlpha2(CountryCode), "invalid_country",
            "الدولة يجب أن تستخدم رمزًا مخصصًا من ISO 3166-1 alpha-2.");
        FunctionalCurrency = Check.Currency(currency); CurrencyScale = scale;
        LegalName = Check.Text(profile!.LegalName, "الاسم القانوني"); Jurisdiction = Check.Text(profile.Jurisdiction, "منطقة الاختصاص");
        FiscalYearStartsOn = profile.FiscalYearStartsOn; City = Check.Text(profile.City, "المدينة"); Street = Check.Text(profile.Street, "الشارع", 300);
        EnglishName = Optional(profile.EnglishName, "الاسم الإنجليزي"); Notes = Optional(profile.Notes, "الملاحظات", 2000);
        CommercialRegistration = Optional(profile.CommercialRegistration, "السجل التجاري", 64); TaxRegistration = Optional(profile.TaxRegistration, "التسجيل الضريبي", 64);
        District = Optional(profile.District, "الحي"); BuildingNumber = Optional(profile.BuildingNumber, "رقم المبنى", 32);
        PostalCode = Optional(profile.PostalCode, "الرمز البريدي", 32); AdditionalNumber = Optional(profile.AdditionalNumber, "الرقم الإضافي", 32);
        Advance();
    }
    private static string? Optional(string? value, string field, int max = 200) => value is null ? null : Check.Text(value, field, max);
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "company_revision_exhausted", "بلغ إصدار الشركة الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
