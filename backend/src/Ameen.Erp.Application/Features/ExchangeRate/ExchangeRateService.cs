using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class ExchangeRateService : ApplicationService
{
    private readonly CurrencyService currencyService;
    public ExchangeRateService(IErpStore store, ITransactionRunner transactions, IClock clock, CurrencyService currencyService) : base(store, transactions, clock)
    {
        this.currencyService = currencyService;
    }


    // Shared by quotations and recurring generation: never select a rate from the future.
    internal async Task<ExchangeRate?> EffectiveExchangeRate(Guid cid, string currency, DateOnly date, CancellationToken ct) =>
        (await store.ReadPage<ExchangeRate>(cid, x => x.CurrencyCode == currency && x.EffectiveDate <= date,
            q => q.OrderByDescending(x => x.EffectiveDate).ThenBy(x => x.Id), 0, 1, ct)).Items.FirstOrDefault();

    public async Task<ExchangeRateQuote> QuoteExchangeRate(Guid cid, Actor actor, string currencyCode, DateOnly date, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(date != default, "exchange_rate_date_required",
            "تاريخ عرض سعر الصرف مطلوب.");
        var company = await Company(cid, ct); var code = Check.Currency(currencyCode);
        await currencyService.RequireCurrency(cid, code, ct);
        if (code == company.FunctionalCurrency) return new(cid, code, company.FunctionalCurrency, date, null, null, 1m, null, true);
        var rate = await EffectiveExchangeRate(cid, code, date, ct);
        Check.That(rate is not null, "exchange_rate_required", "لا يوجد سعر صرف نافذ للعملة في التاريخ المطلوب.");
        return new(cid, code, company.FunctionalCurrency, date, rate!.Id, rate.EffectiveDate, rate.RateToFunctional, rate.Source, false);
    }
    public async Task<ExchangeRateDetail> GetExchangeRate(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct);
        return new(await Required<ExchangeRate>(cid, id, ct), (await Company(cid, ct)).FunctionalCurrency);
    }
    public async Task<Page<ExchangeRate>> SearchExchangeRates(Guid cid, Actor actor, string? currencyCode, DateOnly? from, DateOnly? to,
        string? source, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(from is null || to is null || from <= to, "invalid_date_range", "بداية النطاق بعد نهايته.");
        var code = currencyCode is null ? null : Check.Currency(currencyCode);
        Check.That(source is null || source.Length <= 200, "search_too_long", "عبارة المصدر أطول من المسموح."); source = source?.Trim();
        return await store.ReadPage<ExchangeRate>(cid, x => (code == null || x.CurrencyCode == code) &&
            (from == null || x.EffectiveDate >= from) && (to == null || x.EffectiveDate <= to) && (source == null || x.Source.Contains(source)),
            q => q.OrderByDescending(x => x.EffectiveDate).ThenBy(x => x.CurrencyCode).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AccountUsageReference>> GetExchangeRateUsage(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Required<ExchangeRate>(cid, id, ct);
        var journals = await store.ReadPage<JournalEntry>(cid, x => x.Lines.Any(l => l.ExchangeRateId == id),
            q => q.OrderByDescending(x => x.AccountingDate).ThenBy(x => x.Id), offset, limit, ct);
        return new(journals.Items.Select(x => new AccountUsageReference("Journal", x.Id, x.BranchId, x.Number, x.State.ToString())).ToArray(), journals.TotalCount, offset, limit);
    }
    public async Task<Page<AuditEvent>> ListExchangeRateAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await Authorize(cid, actor, Permissions.Read, null, ct);
        await Required<ExchangeRate>(cid, id, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && x.Action == "exchange-rate.created",
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public Task<CommandReceipt> CreateExchangeRate(Guid cid, Actor actor, string key, CreateExchangeRateRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "exchange-rate.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var company = await Company(cid, token);
            var currency = Check.Currency(request.CurrencyCode);
            Check.That(currency != company.FunctionalCurrency, "functional_rate_fixed", "سعر العملة الوظيفية ثابت ويساوي واحدًا.");
            await currencyService.RequireCurrency(cid, currency, token);
            Check.That(!(await store.Find<ExchangeRate>(cid, x => x.CurrencyCode == currency && x.EffectiveDate == request.EffectiveDate, token)).Any(),
                "duplicate_exchange_rate", "يوجد سعر لهذه العملة في التاريخ نفسه.", ErrorKind.Conflict);
            var rate = new ExchangeRate(cid, currency, request.EffectiveDate, request.RateToFunctional, request.Source);
            store.Add(rate); Audit(cid, actor, "exchange-rate.created", rate, new { rate.CurrencyCode, company.FunctionalCurrency, rate.EffectiveDate, rate.RateToFunctional, rate.Source });
            return Receipt(rate, "Created");
        }, ct);

}
