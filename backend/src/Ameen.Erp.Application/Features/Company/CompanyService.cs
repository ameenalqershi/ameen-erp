using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class CompanyService : ApplicationService
{
    private readonly CurrencyService currencyService;
    public CompanyService(IErpStore store, ITransactionRunner transactions, IClock clock, CurrencyService currencyService) : base(store, transactions, clock)
    {
        this.currencyService = currencyService;
    }


    private static object CompanySnapshot(Company x) => new
    {
        x.Version, x.Revision, x.Code, x.Name, x.CountryCode, x.FunctionalCurrency, x.CurrencyScale, x.IsActive,
        x.EnglishName, x.Notes, x.LegalName, x.Jurisdiction, x.FiscalYearStartsOn, x.CommercialRegistration, x.TaxRegistration,
        x.City, x.District, x.Street, x.BuildingNumber, x.PostalCode, x.AdditionalNumber
    };
    public Task<CommandReceipt> EditCompany(Guid cid, Actor actor, string key, EditCompanyRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "company.edit:" + cid, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var company = await Company(cid, token); Check.Version(company, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            var currency = await currencyService.RequireCurrency(cid, Check.Currency(request.FunctionalCurrency), token);
            var candidate = new Company(cid, request.Code, request.Name, request.CountryCode, currency.Code, currency.Scale, request.Profile);
            if (candidate.Code != company.Code)
                Check.That(!await store.CompanyCodeExists(candidate.Code, token), "company_exists", "رمز الشركة مستخدم بالفعل.", ErrorKind.Conflict);
            if (candidate.FunctionalCurrency != company.FunctionalCurrency || candidate.CurrencyScale != company.CurrencyScale)
                Check.That(await CanChangeFunctionalCurrency(cid, token), "functional_currency_in_use", "تغيير العملة الوظيفية بعد وجود قيود أو أسعار أو قوالب أو أسعار أصناف يتطلب مشروع تحويل معتمد.", ErrorKind.Conflict);
            var years = await store.Find<FiscalYear>(cid, x => true, token);
            Check.That(years.Count == 0 || candidate.FiscalYearStartsOn == years.Min(x => x.StartsOn),
                "company_calendar_in_use", "بداية الشركة يجب أن تطابق أقدم سنة مالية معرفة.", ErrorKind.Conflict);
            var before = CompanySnapshot(company);
            company.Configure(candidate.Code, candidate.Name, candidate.CountryCode, candidate.FunctionalCurrency, candidate.CurrencyScale, request.Profile);
            Audit(cid, actor, "company.edited", company, new { before, after = CompanySnapshot(company), reason });
            return Receipt(company, "Updated");
        }, ct);

    private async Task<bool> CanChangeFunctionalCurrency(Guid cid, CancellationToken ct) =>
        (await store.Find<JournalEntry>(cid, x => true, ct)).Count == 0 &&
        (await store.Find<ExchangeRate>(cid, x => true, ct)).Count == 0 &&
        (await store.Find<RecurringJournalTemplate>(cid, x => true, ct)).Count == 0 &&
        !(await store.Find<Item>(cid, x => x.Units.Any(u => u.SalePrice != null), ct)).Any();

    public Task<CommandReceipt> SetCompanyStatus(Guid cid, Actor actor, string key, SetCompanyStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "company.status:" + cid, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var company = await Company(cid, token); Check.Version(company, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب تغيير الحالة", 500); var before = CompanySnapshot(company);
            company.SetActive(request.IsActive);
            Audit(cid, actor, "company.status-changed", company, new { before, after = CompanySnapshot(company), reason });
            return Receipt(company, company.IsActive ? "Active" : "Inactive");
        }, ct);
    public async Task<CompanyUsage> GetCompanyUsage(Guid cid, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Company(cid, ct);
        var journals = (await store.Find<JournalEntry>(cid, x => true, ct)).Count;
        var templates = (await store.Find<RecurringJournalTemplate>(cid, x => true, ct)).Count;
        var rates = (await store.Find<ExchangeRate>(cid, x => true, ct)).Count;
        return new(cid, (await store.Find<Branch>(cid, x => true, ct)).Count, (await store.Find<Account>(cid, x => true, ct)).Count,
            (await store.Find<Currency>(cid, x => true, ct)).Count, (await store.Find<FiscalYear>(cid, x => true, ct)).Count,
            journals, templates, rates, await CanChangeFunctionalCurrency(cid, ct));
    }
    public async Task<Page<AuditEvent>> ListCompanyAudit(Guid cid, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await Authorize(cid, actor, Permissions.Read, null, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == cid &&
            (x.Action == "company.created" || x.Action == "company.edited" || x.Action == "company.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public Task<CommandReceipt> CreateCompany(Actor actor, string key, CreateCompanyRequest request, CancellationToken ct) =>
        Command(request.Id, actor, key, "company.create", request, _ =>
        {
            Check.That(actor.IsPlatformAdministrator, "platform_admin_required", "إنشاء الشركات يتطلب مسؤول المنصة.", ErrorKind.Forbidden);
            return Task.CompletedTask;
        }, async token =>
        {
            var company = new Company(request.Id, request.Code, request.Name, request.CountryCode, request.FunctionalCurrency, request.CurrencyScale, request.Profile);
            Check.That(await store.GetCompany(company.Id, token) is null && !await store.CompanyCodeExists(company.Code, token),
                "company_exists", "معرف الشركة أو رمزها مستخدم بالفعل.", ErrorKind.Conflict);
            store.Add(company);
            var functional = new Currency(company.Id, company.FunctionalCurrency, company.CurrencyScale, company.FunctionalCurrency);
            store.Add(functional);
            Audit(company.Id, actor, "currency.created", functional, new { after = CurrencyService.CurrencySnapshot(functional), Bootstrap = true });
            // Bootstrap does not grant financial approval or posting implicitly.
            store.Add(new Membership(company.Id, actor.Subject,
                new[] { Permissions.Read, Permissions.Configure, Permissions.Members, Permissions.Audit }, true, Array.Empty<Guid>()));
            Audit(company.Id, actor, "company.created", company, new { after = CompanySnapshot(company) });
            return Receipt(company, "Created");
        }, ct);

    public Task<IReadOnlyList<Company>> ListCompanies(Actor actor, CancellationToken ct) => store.CompaniesFor(actor.Subject, ct);
    public async Task<Company> GetCompany(Guid cid, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct); return await Company(cid, ct);
    }
}
