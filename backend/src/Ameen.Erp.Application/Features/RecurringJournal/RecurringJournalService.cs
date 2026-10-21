using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class RecurringJournalService : ApplicationService
{
    private readonly BranchService branchService;
    private readonly CurrencyService currencyService;
    private readonly ExchangeRateService exchangeRateService;
    private readonly JournalService journalService;
    public RecurringJournalService(IErpStore store, ITransactionRunner transactions, IClock clock, BranchService branchService, CurrencyService currencyService, ExchangeRateService exchangeRateService, JournalService journalService) : base(store, transactions, clock)
    {
        this.branchService = branchService;
        this.currencyService = currencyService;
        this.exchangeRateService = exchangeRateService;
        this.journalService = journalService;
    }


    private async Task<RecurringJournalTemplate> AuthorizeRecurring(Guid cid, Guid id, Actor actor, string permission, CancellationToken ct)
    {
        await Authorize(cid, actor, permission, null, ct);
        var template = await Required<RecurringJournalTemplate>(cid, id, ct);
        await Authorize(cid, actor, permission, template.BranchId, ct);
        return template;
    }

    private async Task<List<RecurringJournalLine>> PrepareRecurringLines(RecurringJournalTemplate template, RecurringLineRequest[]? requests, CancellationToken ct)
    {
        Check.That(requests is not null && requests.Length is >= 2 and <= 200, "invalid_line_count", "القالب يتكون من 2 إلى 200 سطر.");
        var lines = new List<RecurringJournalLine>();
        foreach (var request in requests!)
        {
            Check.That(request is not null, "invalid_line", "سطر القالب لا يمكن أن يكون فارغًا.");
            var code = Check.Currency(request!.CurrencyCode);
            var currency = await currencyService.RequireCurrency(template.CompanyId, code, ct);
            await journalService.ValidateDimensions(template.CompanyId, template.BranchId, request.AccountId, request.CostCenterId, code, ct);
            lines.Add(new RecurringJournalLine(template.CompanyId, template.Id, lines.Count + 1, request.AccountId,
                request.CostCenterId, request.Description, code, request.Debit, request.Credit, currency.Scale));
        }
        return lines;
    }

    public Task<CommandReceipt> CreateRecurringJournal(Guid cid, Actor actor, string key, CreateRecurringJournalRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "recurring.create", request,
            async token => { await Authorize(cid, actor, Permissions.RecurringManage, request.BranchId, token); }, async token =>
        {
            await branchService.ActiveBranch(cid, request.BranchId, token);
            var template = new RecurringJournalTemplate(cid, request.BranchId, request.Name, request.Frequency,
                request.GenerationMode, request.StartsOn, request.EndsOn, actor.Subject, clock.UtcNow);
            Check.That(!(await store.Find<RecurringJournalTemplate>(cid, x => x.BranchId == template.BranchId && x.Name == template.Name, token)).Any(),
                "duplicate_recurring_name", "اسم القالب مستخدم في الفرع نفسه.", ErrorKind.Conflict);
            template.SetLines(await PrepareRecurringLines(template, request.Lines, token));
            store.Add(template);
            Audit(cid, actor, "recurring.created", template, new { template.Version, template.Name, template.BranchId, template.Frequency,
                template.GenerationMode, template.StartsOn, template.EndsOn, template.Lines });
            return Receipt(template, "Active");
        }, ct);

    public Task<CommandReceipt> EditRecurringJournal(Guid cid, Guid id, Actor actor, string key, EditRecurringJournalRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "recurring.edit:" + id, request, token => AuthorizeRecurring(cid, id, actor, Permissions.RecurringManage, token), async token =>
        {
            var template = await Required<RecurringJournalTemplate>(cid, id, token); Check.Version(template, request.ExpectedVersion);
            await branchService.ActiveBranch(cid, template.BranchId, token);
            var before = new { template.Version, template.Name, template.Frequency, template.GenerationMode, template.StartsOn, template.EndsOn, Lines = template.Lines.ToArray() };
            template.Configure(request.Name, request.Frequency, request.GenerationMode, request.StartsOn, request.EndsOn);
            Check.That(!(await store.Find<RecurringJournalTemplate>(cid, x => x.Id != id && x.BranchId == template.BranchId && x.Name == template.Name, token)).Any(),
                "duplicate_recurring_name", "اسم القالب مستخدم في الفرع نفسه.", ErrorKind.Conflict);
            var lines = await PrepareRecurringLines(template, request.Lines, token);
            store.RemoveRecurringLines(template.Lines.ToArray()); template.SetLines(lines);
            Audit(cid, actor, "recurring.edited", template, new { before, after = new { template.Version, template.Name, template.Frequency,
                template.GenerationMode, template.StartsOn, template.EndsOn, template.Lines } });
            return Receipt(template, template.IsActive ? "Active" : "Inactive");
        }, ct);

    public Task<CommandReceipt> SetRecurringStatus(Guid cid, Guid id, Actor actor, string key, SetRecurringStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "recurring.status:" + id, request, token => AuthorizeRecurring(cid, id, actor, Permissions.RecurringManage, token), async token =>
        {
            var template = await Required<RecurringJournalTemplate>(cid, id, token); Check.Version(template, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "السبب", 500); var before = template.IsActive;
            template.SetActive(request.IsActive);
            Audit(cid, actor, "recurring.status-changed", template, new { beforeVersion = request.ExpectedVersion, template.Version, before, after = template.IsActive, reason });
            return Receipt(template, template.IsActive ? "Active" : "Inactive");
        }, ct);

    public Task<CommandReceipt> GenerateRecurringJournal(Guid cid, Guid id, Actor actor, string key, GenerateRecurringJournalRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "recurring.generate:" + id, request, async token =>
        {
            var template = await AuthorizeRecurring(cid, id, actor, Permissions.RecurringGenerate, token);
            await Authorize(cid, actor, Permissions.Draft, template.BranchId, token);
        }, async token =>
        {
            var template = await Required<RecurringJournalTemplate>(cid, id, token); Check.Version(template, request.ExpectedVersion);
            Check.That(template.IsActive, "recurring_inactive", "القالب معطل؛ لا يمكن توليد مسودات جديدة منه.", ErrorKind.Conflict);
            await branchService.ActiveBranch(cid, template.BranchId, token);
            var period = await Required<FiscalPeriod>(cid, request.FiscalPeriodId, token);
            var due = template.DueDateFor(period.StartsOn, period.EndsOn);
            Check.That(due is not null, "recurring_not_due", "لا يوجد موعد لهذا القالب ضمن الفترة ومدة السريان.");
            Check.That(!(await store.Find<RecurringJournalGeneration>(cid, x => x.TemplateId == id && x.FiscalPeriodId == period.Id, token)).Any(),
                "recurring_already_generated", "سبق توليد قيد لهذا القالب والفترة، حتى لو أُلغيت مسودته.", ErrorKind.Conflict);
            var open = await journalService.OpenPeriod(cid, due!.Value, token);
            Check.That(open.Id == period.Id, "period_mismatch", "فترة الموعد لا تطابق الفترة المطلوبة.");
            var company = await Company(cid, token);
            var rates = new Dictionary<string, Guid?>();
            foreach (var code in template.Lines.Select(x => x.CurrencyCode).Distinct())
            {
                if (code == company.FunctionalCurrency) { rates[code] = null; continue; }
                var rate = await exchangeRateService.EffectiveExchangeRate(cid, code, due.Value, token);
                Check.That(rate is not null, "exchange_rate_required", "لا يوجد سعر صرف نافذ لعملة القالب في تاريخ الموعد.");
                rates[code] = rate!.Id;
            }
            var number = await journalService.NextNumber(cid, period.Id, token);
            var journal = new JournalEntry(cid, template.BranchId, period.Id, number.Number, number.Sequence, due.Value, due.Value,
                template.Name + " - " + period.Code, null, actor.Subject, clock.UtcNow);
            journal.SetRecurringSource(template.Id, template.Version);
            var lines = template.Lines.OrderBy(x => x.Position).Select(x => new JournalLineRequest(x.AccountId, x.CostCenterId,
                x.Description, x.CurrencyCode, rates[x.CurrencyCode], x.Debit, x.Credit)).ToArray();
            journal.SetLines(await journalService.PrepareLines(company, journal, lines, token));
            var generation = new RecurringJournalGeneration(cid, template.Id, template.Version, period.Id, journal.Id, due.Value, actor.Subject, clock.UtcNow);
            store.Add(journal); store.Add(generation);
            Audit(cid, actor, "recurring.generated", journal, new { TemplateId = template.Id, TemplateVersion = template.Version, GenerationId = generation.Id,
                FiscalPeriodId = period.Id, DueDate = due.Value, journal.Number, journal.Lines });
            return Receipt(journal, journal.State.ToString(), journal.Number);
        }, ct);

    public Task<RecurringJournalTemplate> GetRecurringJournal(Guid cid, Guid id, Actor actor, CancellationToken ct) =>
        AuthorizeRecurring(cid, id, actor, Permissions.Read, ct);

    public async Task<Page<RecurringJournalSummary>> ListRecurringJournals(Guid cid, Actor actor, Guid? branchId, bool? isActive, int offset, int limit, CancellationToken ct, string? search = null)
    {
        Paging(offset, limit); var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح.");
        search = search?.Trim();
        var branches = await AllowedBranches(cid, member, branchId, ct);
        var page = await store.ReadPage<RecurringJournalTemplate>(cid, x => branches.Contains(x.BranchId) && (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Name.Contains(search)),
            q => q.OrderBy(x => x.Name).ThenBy(x => x.Id), offset, limit, ct);
        return new(page.Items.Select(x => new RecurringJournalSummary(x.Id, x.Version, x.BranchId, x.Name, x.Frequency,
            x.GenerationMode, x.StartsOn, x.EndsOn, x.IsActive, x.Lines.Count)).ToArray(), page.TotalCount, offset, limit);
    }
    public async Task<Page<RecurringJournalGeneration>> ListRecurringGenerations(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeRecurring(cid, id, actor, Permissions.Read, ct);
        return await store.ReadPage<RecurringJournalGeneration>(cid, x => x.TemplateId == id,
            q => q.OrderByDescending(x => x.DueDate).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AuditEvent>> ListRecurringChanges(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        var template = await AuthorizeRecurring(cid, id, actor, Permissions.Read, ct);
        await Authorize(cid, actor, Permissions.Audit, template.BranchId, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
            (x.Action == "recurring.created" || x.Action == "recurring.edited" || x.Action == "recurring.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<IReadOnlyList<RecurringScheduleRow>> RecurringSchedule(Guid cid, Guid id, Guid fiscalYearId, Actor actor, CancellationToken ct)
    {
        var template = await AuthorizeRecurring(cid, id, actor, Permissions.Read, ct);
        await Required<FiscalYear>(cid, fiscalYearId, ct);
        var periods = await store.Find<FiscalPeriod>(cid, x => x.FiscalYearId == fiscalYearId, ct);
        var generations = (await store.Find<RecurringJournalGeneration>(cid, x => x.TemplateId == id, ct)).ToDictionary(x => x.FiscalPeriodId);
        var journalIds = generations.Values.Select(x => x.JournalId).ToArray();
        var journals = (await store.Find<JournalEntry>(cid, x => x.BranchId == template.BranchId && journalIds.Contains(x.Id), ct)).ToDictionary(x => x.Id);
        var result = new List<RecurringScheduleRow>();
        foreach (var period in periods.OrderBy(x => x.StartsOn))
        {
            var due = template.DueDateFor(period.StartsOn, period.EndsOn);
            // A later schedule edit never erases a previous generation from the usage calendar.
            generations.TryGetValue(period.Id, out var generation);
            if (due is null && generation is null) continue;
            var existing = generation is not null && journals.TryGetValue(generation.JournalId, out var journal) ? journal : null;
            result.Add(new(period.Id, period.Code, generation?.DueDate ?? due!.Value, period.IsClosed, generation?.JournalId, existing?.State));
        }
        return result;
    }
}
