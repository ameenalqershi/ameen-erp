using System.Text.Json;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private async Task<FiscalPeriod> OpenPeriod(Guid cid, DateOnly date, CancellationToken ct)
    {
        var periods = await store.Find<FiscalPeriod>(cid, x => x.StartsOn <= date && x.EndsOn >= date, ct);
        Check.That(periods.Count == 1, "period_not_found", "لا توجد فترة مالية وحيدة تغطي تاريخ الأثر.");
        var period = periods[0];
        Check.That(!period.IsClosed, "period_closed", $"الفترة {period.Code} مقفلة.", ErrorKind.Conflict);
        return period;
    }

    private async Task<(long Sequence, string Number)> NextNumber(Guid cid, Guid periodId, CancellationToken ct)
    {
        var period = await Required<FiscalPeriod>(cid, periodId, ct);
        var year = await Required<FiscalYear>(cid, period.FiscalYearId, ct);
        var scope = "JV:" + year.Id;
        var sequence = (await store.Find<NumberSequence>(cid, x => x.Scope == scope, ct)).SingleOrDefault();
        if (sequence is null) { sequence = new NumberSequence(cid, scope); store.Add(sequence); }
        var number = sequence.Next();
        return (number, $"JV-{year.Code}-{number:000000}");
    }

    private async Task<List<JournalLine>> PrepareLines(Company company, JournalEntry journal, JournalLineRequest[]? requests, CancellationToken ct)
    {
        Check.That(requests is not null && requests.Length is >= 2 and <= 200, "invalid_line_count", "القيد يتكون من 2 إلى 200 سطر.");
        var result = new List<JournalLine>();
        foreach (var request in requests!)
        {
            Check.That(request is not null, "invalid_line", "سطر القيد لا يمكن أن يكون فارغًا.");
            var currencyCode = Check.Currency(request!.CurrencyCode);
            var currency = await RequireCurrency(company.Id, currencyCode, ct);
            decimal rate = 1;
            if (currencyCode == company.FunctionalCurrency)
                Check.That(request.ExchangeRateId is null, "functional_rate_fixed", "لا يحدد سعر خارجي للعملة الوظيفية.");
            else
            {
                Check.That(request.ExchangeRateId is not null, "exchange_rate_required", "مرجع سعر الصرف مطلوب للعملة الأجنبية.");
                var exchange = await Required<ExchangeRate>(company.Id, request.ExchangeRateId!.Value, ct);
                Check.That(exchange.CurrencyCode == currencyCode && exchange.EffectiveDate <= journal.AccountingDate,
                    "exchange_rate_mismatch", "سعر الصرف لا يخص العملة أو يبدأ بعد تاريخ القيد.");
                // A historical rate must be the most recent effective rate as of the journal date.
                Check.That(!(await store.Find<ExchangeRate>(company.Id, x => x.CurrencyCode == currencyCode &&
                    x.EffectiveDate <= journal.AccountingDate && x.EffectiveDate > exchange.EffectiveDate, ct)).Any(),
                    "stale_exchange_rate", "اختر أحدث سعر صرف نافذ في تاريخ القيد.");
                rate = exchange.RateToFunctional;
            }
            var line = new JournalLine(company.Id, journal.Id, result.Count + 1, request.AccountId,
                request.CostCenterId, request.Description, currencyCode, request.ExchangeRateId, rate,
                request.Debit, request.Credit, currency.Scale, company.CurrencyScale);
            await ValidateLine(journal, line, ct); result.Add(line);
        }
        return result;
    }

    private Task ValidateLine(JournalEntry journal, JournalLine line, CancellationToken ct) =>
        ValidateDimensions(journal.CompanyId, journal.BranchId, line.AccountId, line.CostCenterId, line.CurrencyCode, ct);

    private async Task ValidateDimensions(Guid cid, Guid branchId, Guid accountId, Guid? costCenterId, string currency, CancellationToken ct)
    {
        await RequireCurrency(cid, currency, ct);
        var account = await Required<Account>(cid, accountId, ct);
        Check.That(account.IsActive && account.IsPosting, "account_not_postable", "الحساب غير فعال أو رئيسي ولا يقبل الحركة.");
        Check.That(!account.IsControl, "control_account_protected", "الحساب الرقابي يقبل أثر دفتره المساعد فقط.");
        Check.That(account.CurrencyCode is null || account.CurrencyCode == currency,
            "account_currency_mismatch", "عملة السطر غير مسموحة للحساب.");
        Check.That(!account.RequiresCostCenter || costCenterId is not null,
            "cost_center_required", "مركز التكلفة مطلوب لهذا الحساب.");
        if (costCenterId is Guid centerId)
        {
            var center = await Required<CostCenter>(cid, centerId, ct);
            Check.That(center.IsActive && (center.BranchId is null || center.BranchId == branchId),
                "cost_center_mismatch", "المركز غير فعال أو تابع لفرع آخر.");
        }
    }

    private async Task ValidateForPosting(JournalEntry journal, CancellationToken ct)
    {
        await ActiveBranch(journal.CompanyId, journal.BranchId, ct);
        var period = await OpenPeriod(journal.CompanyId, journal.AccountingDate, ct);
        Check.That(period.Id == journal.FiscalPeriodId, "period_mismatch", "الفترة لا تطابق تاريخ القيد.");
        foreach (var line in journal.Lines) await ValidateLine(journal, line, ct);
        journal.ValidateBalance();
    }

    public Task<CommandReceipt> CreateJournal(Guid cid, Actor actor, string key, CreateJournalRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "journal.create", request,
            async token => { await Authorize(cid, actor, Permissions.Draft, request.BranchId, token); }, async token =>
        {
            var company = await Company(cid, token); await ActiveBranch(cid, request.BranchId, token);
            var period = await OpenPeriod(cid, request.AccountingDate, token);
            var number = await NextNumber(cid, period.Id, token);
            var journal = new JournalEntry(cid, request.BranchId, period.Id, number.Number, number.Sequence,
                request.DocumentDate, request.AccountingDate, request.Description, request.ExternalReference, actor.Subject, clock.UtcNow);
            journal.SetLines(await PrepareLines(company, journal, request.Lines, token));
            // An unbalanced draft is allowed; submission/approval/posting require exact balance.
            store.Add(journal);
            Audit(cid, actor, "journal.created", journal, new { journal.Number, journal.AccountingDate, journal.BranchId, journal.Lines });
            return Receipt(journal, journal.State.ToString(), journal.Number);
        }, ct);

    public Task<CommandReceipt> EditJournal(Guid cid, Guid id, Actor actor, string key, EditJournalRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "journal.edit:" + id, request,
            token => AuthorizeJournal(cid, id, actor, Permissions.Draft, token), async token =>
        {
            var journal = await Required<JournalEntry>(cid, id, token);
            Check.Version(journal, request.ExpectedVersion); journal.RequireState(JournalState.Draft);
            Check.That(journal.ReversesJournalId is null, "reversal_immutable", "سطور العكس نسخة معكوسة ثابتة من المصدر؛ ألغِ مسودة العكس عند الحاجة.", ErrorKind.Conflict);
            await OpenPeriod(cid, journal.AccountingDate, token);
            var company = await Company(cid, token);
            var newLines = await PrepareLines(company, journal, request.Lines, token);
            var before = new { journal.Description, Lines = journal.Lines.ToArray() };
            store.RemoveLines(journal.Lines.ToArray());
            journal.EditDescription(request.Description); journal.SetLines(newLines);
            Audit(cid, actor, "journal.edited", journal, new { before, after = new { journal.Description, journal.Lines } });
            return Receipt(journal, journal.State.ToString(), journal.Number);
        }, ct);

    public Task<CommandReceipt> TransitionJournal(Guid cid, Guid id, Actor actor, string key,
        TransitionRequest request, string action, CancellationToken ct)
    {
        var permission = action switch
        {
            "submit" or "cancel" => Permissions.Draft,
            "approve" or "return" => Permissions.Approve,
            "post" => Permissions.Post,
            _ => throw new BusinessException("invalid_transition", "الإجراء غير معروف.")
        };
        return Command(cid, actor, key, "journal." + action + ":" + id, request,
            token => AuthorizeJournal(cid, id, actor, permission, token), async token =>
        {
            var journal = await Required<JournalEntry>(cid, id, token); Check.Version(journal, request.ExpectedVersion);
            var before = journal.State;
            switch (action)
            {
                case "submit":
                    await ValidateForPosting(journal, token); journal.Submit(); break;
                case "approve":
                    await ValidateForPosting(journal, token);
                    journal.Approve(actor.Subject, (await Company(cid, token)).RequireSeparateApprover); break;
                case "return":
                    Check.Text(request.Reason, "سبب الإعادة", 500); journal.ReturnToDraft(); break;
                case "cancel":
                    Check.Text(request.Reason, "سبب الإلغاء", 500); journal.Cancel();
                    if (journal.ReversesJournalId is Guid cancelledOriginal)
                        (await Required<JournalEntry>(cid, cancelledOriginal, token)).ReleaseReversal(journal.Id);
                    break;
                case "post":
                    await ValidateForPosting(journal, token);
                    journal.Post(clock.UtcNow);
                    if (journal.ReversesJournalId is Guid originalId)
                    {
                        var original = await Required<JournalEntry>(cid, originalId, token);
                        original.LinkReversal(journal.Id);
                        Audit(cid, actor, "journal.reversed", original, new { ReversalId = journal.Id, journal.ReversalReason });
                    }
                    store.Add(new OutboxEvent(cid, "ledger.journal-posted.v1", journal.Id, clock.UtcNow,
                        JsonSerializer.Serialize(new { journal.Id, journal.Number, journal.BranchId, journal.AccountingDate, journal.ReversesJournalId }, JsonDefaults.Options)));
                    break;
            }
            Audit(cid, actor, "journal." + action, journal, new { before, after = journal.State, request.Reason });
            return Receipt(journal, journal.State.ToString(), journal.Number);
        }, ct);
    }

    public Task<CommandReceipt> ReverseJournal(Guid cid, Guid id, Actor actor, string key, ReverseJournalRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "journal.reverse:" + id, request,
            token => AuthorizeJournal(cid, id, actor, Permissions.Reverse, token), async token =>
        {
            var original = await Required<JournalEntry>(cid, id, token); Check.Version(original, request.ExpectedVersion);
            original.RequireState(JournalState.Posted);
            Check.That(request.AccountingDate >= original.AccountingDate, "reversal_before_original", "تاريخ العكس لا يسبق تاريخ الأصل.");
            var reason = Check.Text(request.Reason, "سبب العكس", 500);
            var period = await OpenPeriod(cid, request.AccountingDate, token);
            var number = await NextNumber(cid, period.Id, token);
            var reversal = new JournalEntry(cid, original.BranchId, period.Id, number.Number, number.Sequence,
                request.AccountingDate, request.AccountingDate, "عكس القيد " + original.Number, original.Number, actor.Subject, clock.UtcNow);
            reversal.SetReversalSource(original.Id, reason);
            reversal.SetLines(original.Lines.Select(x => x.Opposite(reversal.Id)));
            original.ReserveReversal(reversal.Id); store.Add(reversal);
            Audit(cid, actor, "journal.reversal-drafted", reversal, new { OriginalId = id, reason });
            return Receipt(reversal, reversal.State.ToString(), reversal.Number);
        }, ct);
    public async Task<JournalEntry> GetJournal(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeJournal(cid, id, actor, Permissions.Read, ct); return await Required<JournalEntry>(cid, id, ct);
    }
    public async Task<Page<JournalSummary>> ListJournals(Guid cid, Actor actor, Guid? branchId,
        JournalState? state, int offset, int limit, CancellationToken ct, JournalSource? source = null)
    {
        Paging(offset, limit);
        if (state is not null) Check.That(Enum.IsDefined(typeof(JournalState), state.Value), "invalid_state", "حالة القيد غير معروفة.");
        if (source is not null) Check.That(Enum.IsDefined(source.Value), "invalid_source", "مصدر القيد غير معروف.");
        var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        var branches = await AllowedBranches(cid, member, branchId, ct);
        var page = await store.ReadPage<JournalEntry>(cid, x => branches.Contains(x.BranchId) && (state == null || x.State == state) && (source == null || x.Source == source),
            q => q.OrderByDescending(x => x.AccountingDate).ThenByDescending(x => x.Sequence).ThenBy(x => x.Id), offset, limit, ct);
        return new(page.Items.Select(x => new JournalSummary(x.Id, x.Version, x.Number, x.BranchId, x.AccountingDate,
                x.Description, x.State, x.Lines.Sum(l => l.FunctionalDebit), x.Lines.Sum(l => l.FunctionalCredit), x.Source, x.RecurringTemplateId)).ToArray(), page.TotalCount, offset, limit);
    }
}
