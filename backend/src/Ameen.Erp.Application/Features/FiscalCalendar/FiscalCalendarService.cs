using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class FiscalCalendarService : ApplicationService
{
    public FiscalCalendarService(IErpStore store, ITransactionRunner transactions, IClock clock) : base(store, transactions, clock)
    {
    }


    private static IReadOnlyList<FiscalPeriod> PrepareFiscalPeriods(FiscalYear year, FiscalPeriodRequest[]? requests)
    {
        var periods = new List<FiscalPeriod>();
        if (requests is null)
        {
            var current = year.StartsOn; var index = 1;
            while (current <= year.EndsOn)
            {
                var monthEnd = new DateOnly(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
                var end = monthEnd < year.EndsOn ? monthEnd : year.EndsOn;
                periods.Add(new(year.CompanyId, year.Id, $"{year.Code}-{index:00}", current, end));
                if (end == year.EndsOn) break;
                current = end.AddDays(1); index++;
            }
            return periods;
        }
        // At least one day per period; 367 is the existing maximum fiscal-year length.
        Check.That(requests.Length is >= 1 and <= 367, "invalid_period_count", "عدد الفترات من 1 إلى 367.");
        foreach (var request in requests)
        {
            Check.That(request is not null, "invalid_period", "الفترة لا يمكن أن تكون فارغة.");
            periods.Add(new(year.CompanyId, year.Id, request!.Code, request.StartsOn, request.EndsOn));
        }
        periods = periods.OrderBy(x => x.StartsOn).ToList();
        Check.That(periods.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() == periods.Count,
            "duplicate_period_code", "رموز الفترات يجب أن تكون فريدة.");
        var expectedStart = year.StartsOn.DayNumber;
        foreach (var period in periods)
        {
            Check.That(period.StartsOn.DayNumber == expectedStart && period.EndsOn <= year.EndsOn,
                "invalid_period_coverage", "الفترات يجب أن تغطي السنة دون تداخل أو فجوات أو تجاوز الحدود.");
            expectedStart = period.EndsOn.DayNumber + 1;
        }
        Check.That(expectedStart == year.EndsOn.DayNumber + 1, "invalid_period_coverage", "الفترات لا تغطي نهاية السنة.");
        return periods;
    }

    public async Task<FiscalCalendar> GetFiscalCalendar(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct);
        var year = await Required<FiscalYear>(cid, id, ct);
        var periods = (await store.Find<FiscalPeriod>(cid, x => x.FiscalYearId == id, ct)).OrderBy(x => x.StartsOn).ToArray();
        var closed = periods.Count(x => x.IsClosed);
        var state = closed == 0 ? FiscalCalendarState.Open : closed == periods.Length ? FiscalCalendarState.Closed : FiscalCalendarState.PartiallyClosed;
        return new(year, periods, state, true);
    }
    public async Task<Page<FiscalYear>> SearchFiscalYears(Guid cid, Actor actor, string? search, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<FiscalYear>(cid, x => search == null || x.Code.Contains(search),
            q => q.OrderByDescending(x => x.StartsOn).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<FiscalYearUsage> GetFiscalYearUsage(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct); await Required<FiscalYear>(cid, id, ct);
        var periods = (await store.Find<FiscalPeriod>(cid, x => x.FiscalYearId == id, ct)).Select(x => x.Id).ToArray();
        var journals = await store.Find<JournalEntry>(cid, x => periods.Contains(x.FiscalPeriodId), ct);
        var generations = await store.Find<RecurringJournalGeneration>(cid, x => periods.Contains(x.FiscalPeriodId), ct);
        return new(id, journals.Count, journals.Count(x => x.State is JournalState.Draft or JournalState.Submitted or JournalState.Approved), generations.Count);
    }
    public async Task<Page<AuditEvent>> ListFiscalYearAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await Authorize(cid, actor, Permissions.Read, null, ct); await Required<FiscalYear>(cid, id, ct);
        var periods = (await store.Find<FiscalPeriod>(cid, x => x.FiscalYearId == id, ct)).Select(x => x.Id).ToArray();
        return await store.ReadPage<AuditEvent>(cid, x => (x.ResourceId == id && x.Action == "fiscal-year.created") ||
            (periods.Contains(x.ResourceId) && (x.Action == "period.closed" || x.Action == "period.reopened")),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public Task<CommandReceipt> CreateFiscalYear(Guid cid, Actor actor, string key, CreateFiscalYearRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "fiscal-year.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var company = await Company(cid, token);
            if (!(await store.Find<FiscalYear>(cid, x => true, token)).Any())
                Check.That(company.FiscalYearStartsOn == request.StartsOn, "company_fiscal_start_mismatch", "بداية أول سنة يجب أن تطابق البداية المحددة في بطاقة الشركة.");
            Check.That(request.StartsOn >= company.FiscalYearStartsOn, "company_fiscal_start_mismatch", "السنة لا تسبق البداية المحددة في بطاقة الشركة.");
            var year = new FiscalYear(cid, request.Code, request.StartsOn, request.EndsOn);
            Check.That(!(await store.Find<FiscalYear>(cid, x => x.Code == year.Code || (x.StartsOn <= year.EndsOn && x.EndsOn >= year.StartsOn), token)).Any(),
                "fiscal_year_overlap", "السنة تتداخل مع سنة قائمة أو تكرر رمزها.", ErrorKind.Conflict);
            var periods = PrepareFiscalPeriods(year, request.Periods);
            var codes = periods.Select(x => x.Code).ToArray();
            Check.That(!(await store.Find<FiscalPeriod>(cid, x => codes.Contains(x.Code), token)).Any(),
                "duplicate_period_code", "رمز الفترة مستخدم في الشركة.", ErrorKind.Conflict);
            store.Add(year); foreach (var period in periods) store.Add(period);
            Audit(cid, actor, "fiscal-year.created", year, new { year.Code, year.StartsOn, year.EndsOn,
                Periods = periods.Select(x => new { x.Id, x.Code, x.StartsOn, x.EndsOn }), GeneralLedgerOnly = true });
            return Receipt(year, "Created");
        }, ct);

}
