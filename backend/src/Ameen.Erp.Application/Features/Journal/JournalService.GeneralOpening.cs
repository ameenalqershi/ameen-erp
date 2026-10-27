using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class JournalService
{
    private void TrackOpeningReversal(JournalEntry original, Guid reversalId, Actor actor, string action)
    {
        original.AdvanceRevision(clock.UtcNow);
        if (original.Source != JournalSource.GeneralOpening) return;
        Audit(original.CompanyId, actor, "general-opening.reversal-" + action, original,
            new { ReversalId = reversalId, original.Revision, original.LastModifiedAt, original.OpeningRevision, original.OpeningModifiedAt,
                original.PendingReversalJournalId, original.ReversalJournalId, original.State });
    }

    private async Task ValidateOpeningRate(Company company, string currencyCode, decimal declaredRate, DateOnly accountingDate, CancellationToken ct)
    {
        await currencyService.RequireCurrency(company.Id, currencyCode, ct);
        Check.That(declaredRate > 0 && declaredRate < 1_000_000_000m && decimal.Round(declaredRate, 10) == declaredRate,
            "invalid_exchange_rate", "سعر صرف الافتتاح غير صالح.");
        if (currencyCode == company.FunctionalCurrency)
        {
            Check.That(declaredRate == 1m, "opening_functional_currency_only",
                "سعر صرف الافتتاح بالعملة الوظيفية يجب أن يساوي واحدًا.");
            return;
        }
        var available = await store.Find<ExchangeRate>(company.Id,
            x => x.CurrencyCode == currencyCode && x.EffectiveDate <= accountingDate, ct);
        var latest = available.OrderByDescending(x => x.EffectiveDate).ThenBy(x => x.Id).FirstOrDefault();
        Check.That(latest is not null, "exchange_rate_required", "لا يوجد سعر صرف نافذ في تاريخ الافتتاح.");
        Check.That(latest!.RateToFunctional == declaredRate, "opening_rate_mismatch",
            "سعر الافتتاح لا يطابق أحدث سعر نافذ للعملة وتاريخ القيد.");
    }

    public Task<CommandReceipt> CreateGeneralOpening(Guid cid, Actor actor, string key, CreateGeneralOpeningRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "general-opening.create", request,
            async token => { await Authorize(cid, actor, Permissions.Draft, request.BranchId, token); }, async token =>
        {
            var company = await Company(cid, token); await branchService.ActiveBranch(cid, request.BranchId, token);
            await ValidateOpeningRate(company, Check.Currency(request.CurrencyCode), request.ExchangeRate, request.AccountingDate, token);
            var period = await OpenPeriod(cid, request.AccountingDate, token); var number = await NextNumber(cid, period.Id, token);
            var journal = new JournalEntry(cid, request.BranchId, period.Id, number.Number, number.Sequence, request.DocumentDate,
                request.AccountingDate, request.Description, request.ExternalReference, actor.Subject, clock.UtcNow);
            journal.SetGeneralOpeningSource(request.OperationStartsOn, request.PreviousTrialBalanceReference, request.CurrencyCode, request.ExchangeRate, clock.UtcNow);
            journal.SetLines(await PrepareLines(company, journal, request.Lines, token));
            store.Add(journal);
            Audit(cid, actor, "general-opening.created", journal, new { journal.Number, journal.OperationStartsOn, journal.PreviousTrialBalanceReference, journal.Lines });
            return Receipt(journal, journal.State.ToString(), journal.Number);
        }, ct);
    public async Task<JournalEntry> GetGeneralOpening(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var journal = await GetJournal(cid, id, actor, ct);
        Check.That(journal.Source == JournalSource.GeneralOpening, "general_opening_required", "القيد ليس افتتاحًا عامًا.");
        return journal;
    }
    public Task<CommandReceipt> EditGeneralOpening(Guid cid, Guid id, Actor actor, string key, EditGeneralOpeningRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "general-opening.edit:" + id, request,
            token => AuthorizeDraftEdit(cid, id, actor, request.Header, token), async token =>
        {
            var journal = await Required<JournalEntry>(cid, id, token); Check.Version(journal, request.ExpectedVersion);
            Check.That(journal.Source == JournalSource.GeneralOpening, "general_opening_required", "القيد ليس افتتاحًا عامًا.");
            journal.RequireState(JournalState.Draft); await OpenPeriod(cid, journal.AccountingDate, token);
            var before = new { Header = HeaderSnapshot(journal), journal.Description, journal.PreviousTrialBalanceReference, Lines = journal.Lines.ToArray() };
            await ReplaceDraftHeader(journal, request.Header, token);
            var lines = await PrepareLines(await Company(cid, token), journal, request.Lines, token);
            journal.EditOpeningReference(request.PreviousTrialBalanceReference); journal.EditDescription(request.Description);
            store.RemoveLines(journal.Lines.ToArray()); journal.SetLines(lines); journal.AdvanceRevision(clock.UtcNow);
            Audit(cid, actor, "general-opening.edited", journal, new { before, after = new { Header = HeaderSnapshot(journal), journal.Description, journal.PreviousTrialBalanceReference, journal.Lines } });
            return Receipt(journal, journal.State.ToString(), journal.Number);
        }, ct);
    public async Task<GeneralOpeningSettlementPreview> OpeningSettlement(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var journal = await GetGeneralOpening(cid, id, actor, ct);
        var period = await Required<FiscalPeriod>(cid, journal.FiscalPeriodId, ct);
        // Existing subledger opening sources identify historical clearing accounts; do not rely on current settings.
        var sources = await store.Find<InventoryOpening>(cid, x => x.BranchId == journal.BranchId && x.State == InventoryOpeningState.Posted &&
            x.AccountingDate <= journal.AccountingDate, ct);
        var accounts = sources.Where(x => x.ClearingAccountId != null).Select(x => x.ClearingAccountId!.Value).Distinct().OrderBy(x => x).ToArray();
        var posted = await store.Find<JournalEntry>(cid, x => x.BranchId == journal.BranchId && x.AccountingDate <= journal.AccountingDate &&
            (x.State == JournalState.Posted || x.State == JournalState.Reversed), ct);
        var candidate = journal.State is JournalState.Draft or JournalState.Submitted or JournalState.Approved;
        var rows = accounts.Select(account =>
        {
            var balance = posted.SelectMany(x => x.Lines).Where(x => x.AccountId == account).Sum(x => x.FunctionalDebit - x.FunctionalCredit);
            var change = candidate ? journal.Lines.Where(x => x.AccountId == account).Sum(x => x.FunctionalDebit - x.FunctionalCredit) : 0;
            return new OpeningClearingBalance(account, balance, change, balance + change, balance + change == 0);
        }).ToArray();
        return new(id, journal.Version, period.FiscalYearId, journal.OpeningCurrencyCode!, journal.AccountingDate,
            candidate, rows.All(x => x.IsZero), false, rows, clock.UtcNow);
    }
}
