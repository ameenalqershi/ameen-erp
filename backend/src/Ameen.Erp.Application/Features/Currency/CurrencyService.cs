using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private static object CurrencySnapshot(Currency x) => new
    { x.Version, x.Revision, x.Code, x.Scale, x.Name, x.EnglishName, x.Notes, x.DisplaySymbol, x.FractionName, x.IsActive };

    private async Task<CurrencyUsage> CurrencyReferences(Guid cid, Currency currency, CancellationToken ct) => new(currency.Id,
        (await Company(cid, ct)).FunctionalCurrency == currency.Code,
        (await store.Find<Account>(cid, x => x.CurrencyCode == currency.Code, ct)).Count,
        (await store.Find<ExchangeRate>(cid, x => x.CurrencyCode == currency.Code, ct)).Count,
        (await store.Find<JournalEntry>(cid, x => x.Lines.Any(l => l.CurrencyCode == currency.Code), ct)).Count,
        (await store.Find<RecurringJournalTemplate>(cid, x => x.Lines.Any(l => l.CurrencyCode == currency.Code), ct)).Count);

    public Task<CommandReceipt> EditCurrency(Guid cid, Guid id, Actor actor, string key, EditCurrencyRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "currency.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var currency = await Required<Currency>(cid, id, token); Check.Version(currency, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            if (currency.Scale != request.Scale)
            {
                var usage = await CurrencyReferences(cid, currency, token);
                Check.That(!usage.IsFunctionalCurrency, "functional_currency_scale_fixed", "دقة العملة الوظيفية تتبع إعداد الشركة ولا تعدل من بطاقة العملة.", ErrorKind.Conflict);
                Check.That(usage.AccountCount + usage.ExchangeRateCount + usage.JournalCount + usage.RecurringTemplateCount == 0,
                    "currency_precision_in_use", "لا يمكن تغيير دقة عملة مستخدمة في حساب أو سعر أو قيد أو قالب.", ErrorKind.Conflict);
            }
            var before = CurrencySnapshot(currency);
            currency.Configure(request.Scale, request.Name, request.EnglishName, request.Notes, request.DisplaySymbol, request.FractionName);
            Audit(cid, actor, "currency.edited", currency, new { before, after = CurrencySnapshot(currency), reason });
            return Receipt(currency, "Updated");
        }, ct);

    public Task<CommandReceipt> SetCurrencyStatus(Guid cid, Guid id, Actor actor, string key, SetCurrencyStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "currency.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var currency = await Required<Currency>(cid, id, token); Check.Version(currency, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب تغيير الحالة", 500);
            Check.That(request.IsActive || (await Company(cid, token)).FunctionalCurrency != currency.Code,
                "functional_currency_required", "لا يمكن تعطيل العملة الوظيفية للشركة.", ErrorKind.Conflict);
            var before = CurrencySnapshot(currency); currency.SetActive(request.IsActive);
            Audit(cid, actor, "currency.status-changed", currency, new { before, after = CurrencySnapshot(currency), reason });
            return Receipt(currency, currency.IsActive ? "Active" : "Inactive");
        }, ct);

    public async Task<Currency> GetCurrency(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct); return await Required<Currency>(cid, id, ct);
    }
    public async Task<Page<Currency>> SearchCurrencies(Guid cid, Actor actor, string? search, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<Currency>(cid, x => (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<CurrencyUsage> GetCurrencyUsage(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        return await CurrencyReferences(cid, await Required<Currency>(cid, id, ct), ct);
    }
    public async Task<Page<AuditEvent>> ListCurrencyAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await Authorize(cid, actor, Permissions.Read, null, ct); await Required<Currency>(cid, id, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
            (x.Action == "currency.created" || x.Action == "currency.edited" || x.Action == "currency.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public Task<CommandReceipt> CreateCurrency(Guid cid, Actor actor, string key, CreateCurrencyRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "currency.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var currency = new Currency(cid, request.Code, request.Scale, request.Name, request.EnglishName, request.Notes, request.DisplaySymbol, request.FractionName);
            Check.That(!(await store.Find<Currency>(cid, x => x.Code == currency.Code, token)).Any(), "duplicate_currency", "العملة معرفة مسبقًا.", ErrorKind.Conflict);
            store.Add(currency); Audit(cid, actor, "currency.created", currency, new { after = CurrencySnapshot(currency) });
            return Receipt(currency, "Created");
        }, ct);

    private async Task<Currency> RequireCurrency(Guid cid, string code, CancellationToken ct)
    {
        var currency = (await store.Find<Currency>(cid, x => x.Code == code, ct)).SingleOrDefault()
            ?? throw new BusinessException("currency_not_found", "العملة غير معرفة في الشركة.");
        Check.That(currency.IsActive, "currency_inactive", "العملة معطلة.", ErrorKind.Conflict);
        return currency;
    }

}
