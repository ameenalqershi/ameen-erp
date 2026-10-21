namespace Ameen.Erp.Domain;

public sealed record CompanyProfile(string LegalName, string Jurisdiction, DateOnly FiscalYearStartsOn,
    string City, string Street, string? EnglishName = null, string? Notes = null, string? CommercialRegistration = null,
    string? TaxRegistration = null, string? District = null, string? BuildingNumber = null,
    string? PostalCode = null, string? AdditionalNumber = null);

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
        Check.That(CountryCode.Length == 2 && CountryCode.All(c => c >= 'A' && c <= 'Z'), "invalid_country", "رمز الدولة من حرفين.");
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

public sealed class Currency : CompanyEntity
{
    public string Code { get; private set; } = "";
    public int Scale { get; private set; }
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public string? DisplaySymbol { get; private set; }
    public string? FractionName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    private Currency() { }
    public Currency(Guid companyId, string code, int scale, string name, string? englishName = null,
        string? notes = null, string? displaySymbol = null, string? fractionName = null) : base(companyId)
    {
        Code = Check.Currency(code);
        Configure(scale, name, englishName, notes, displaySymbol, fractionName);
    }
    public void Configure(int scale, string name, string? englishName, string? notes, string? displaySymbol, string? fractionName)
    {
        Check.That(scale is >= 0 and <= 6, "invalid_scale", "دقة العملة بين صفر وست خانات.");
        Scale = scale; Name = Check.Text(name, "اسم العملة");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        DisplaySymbol = displaySymbol is null ? null : Check.Text(displaySymbol, "رمز العرض", 20);
        FractionName = fractionName is null ? null : Check.Text(fractionName, "اسم الجزء", 100);
        Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "currency_revision_exhausted", "بلغ إصدار العملة الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}

public sealed class Branch : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    private Branch() { }
    public Branch(Guid companyId, string code, string name) : base(companyId)
    {
        Code = Check.Code(code, "رمز الفرع"); Name = Check.Text(name, "اسم الفرع");
    }
}

public sealed class Membership : CompanyEntity
{
    public string Subject { get; private set; } = "";
    public string[] GrantedPermissions { get; private set; } = Array.Empty<string>();
    public Guid[] BranchIds { get; private set; } = Array.Empty<Guid>();
    public bool AllBranches { get; private set; }
    public bool IsActive { get; private set; } = true;
    private Membership() { }
    public Membership(Guid companyId, string subject, IEnumerable<string> permissions,
        bool allBranches, IEnumerable<Guid> branches) : base(companyId)
    {
        Subject = Check.Text(subject, "معرف المستخدم لدى جهة الهوية");
        Configure(permissions, allBranches, branches);
    }
    public void Configure(IEnumerable<string> permissions, bool allBranches, IEnumerable<Guid> branches)
    {
        var grants = permissions.Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray();
        Check.That(grants.Length > 0 && grants.All(x => Permissions.All.Contains(x)), "invalid_permissions", "قائمة الصلاحيات غير صالحة.");
        var scope = branches.Distinct().ToArray();
        Check.That(allBranches || scope.Length > 0, "empty_branch_scope", "حدد الفروع المسموحة.");
        GrantedPermissions = grants; AllBranches = allBranches;
        BranchIds = allBranches ? Array.Empty<Guid>() : scope;
        Touch();
    }
    public bool Has(string permission) => IsActive && GrantedPermissions.Contains(permission, StringComparer.Ordinal);
    public bool Allows(Guid branch) => AllBranches || BranchIds.Contains(branch);
    public void SetActive(bool active) { IsActive = active; Touch(); }
}

public sealed class ExchangeRate : CompanyEntity
{
    public string CurrencyCode { get; private set; } = "";
    public DateOnly EffectiveDate { get; private set; }
    public decimal RateToFunctional { get; private set; }
    public string Source { get; private set; } = "";
    private ExchangeRate() { }
    public ExchangeRate(Guid companyId, string currency, DateOnly date, decimal rate, string source) : base(companyId)
    {
        Check.That(rate > 0 && rate < 1_000_000_000m && decimal.Round(rate, 10) == rate,
            "invalid_exchange_rate", "سعر الصرف موجب وبحد أقصى عشر خانات عشرية.");
        CurrencyCode = Check.Currency(currency); EffectiveDate = date;
        RateToFunctional = rate; Source = Check.Text(source, "مصدر السعر");
    }
}

public sealed class FiscalYear : CompanyEntity
{
    public string Code { get; private set; } = "";
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    private FiscalYear() { }
    public FiscalYear(Guid companyId, string code, DateOnly start, DateOnly end) : base(companyId)
    {
        Check.That(end >= start && end.DayNumber - start.DayNumber < 367, "invalid_fiscal_year", "مدة السنة المالية من يوم إلى 367 يومًا.");
        Code = Check.Code(code, "رمز السنة", 16); StartsOn = start; EndsOn = end;
    }
}

public sealed class FiscalPeriod : CompanyEntity
{
    public Guid FiscalYearId { get; private set; }
    public string Code { get; private set; } = "";
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public bool IsClosed { get; private set; }
    private FiscalPeriod() { }
    public FiscalPeriod(Guid companyId, Guid yearId, string code, DateOnly start, DateOnly end) : base(companyId)
    {
        Check.That(end >= start, "invalid_period", "نهاية الفترة قبل بدايتها.");
        FiscalYearId = yearId; Code = Check.Code(code, "رمز الفترة"); StartsOn = start; EndsOn = end;
    }
    public void SetClosed(bool closed)
    {
        Check.That(IsClosed != closed, "period_state_conflict", "الفترة في الحالة المطلوبة بالفعل.", ErrorKind.Conflict);
        IsClosed = closed; Touch();
    }
}

public sealed class CostCenter : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? ResponsibleMembershipId { get; private set; }
    public int Revision { get; private set; }
    public bool IsActive { get; private set; } = true;
    private CostCenter() { }
    public CostCenter(Guid companyId, string code, string name, Guid? branchId, Guid? parentId = null,
        Guid? responsibleMembershipId = null, string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, branchId, parentId, responsibleMembershipId, englishName, notes);
    public void Configure(string code, string name, Guid? branchId, Guid? parentId,
        Guid? responsibleMembershipId, string? englishName, string? notes)
    {
        Code = Check.Code(code, "رمز المركز"); Name = Check.Text(name, "اسم المركز"); BranchId = branchId;
        ParentId = parentId; ResponsibleMembershipId = responsibleMembershipId;
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000); Advance();
    }
    public void SetActive(bool active) { IsActive = active; Advance(); }
    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "center_revision_exhausted", "بلغ إصدار المركز الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
