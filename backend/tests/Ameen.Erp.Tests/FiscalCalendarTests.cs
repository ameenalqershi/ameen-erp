using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class FiscalCalendarTests
{
    public static CreateFiscalYearRequest Request() => new("2027", new(2027, 1, 1), new(2027, 12, 31),
        new[] { new FiscalPeriodRequest("2027-H1", new(2027, 1, 1), new(2027, 6, 30)), new FiscalPeriodRequest("2027-H2", new(2027, 7, 1), new(2027, 12, 31)) });
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS03: custom periods are sorted, exact and audited", async f =>
        {
            var request = Request() with { Periods = Request().Periods!.Reverse().ToArray() };
            var r = await f.Service.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), request, default);
            var calendar = await f.Service.GetFiscalCalendar(f.CompanyId, r.ResourceId, f.Limited, default);
            Ensure(calendar.Periods.Count == 2 && calendar.Periods[0].Code == "2027-H1" && calendar.State == FiscalCalendarState.Open && calendar.GeneralLedgerOnly);
            Ensure((await f.Service.SearchFiscalYears(f.CompanyId, f.Owner, "2027", 0, 10, default)).TotalCount == 1);
            var audit = await f.Service.ListFiscalYearAudit(f.CompanyId, r.ResourceId, f.Owner, 0, 10, default);
            Ensure(audit.TotalCount == 1 && audit.Items[0].Details.Contains("2027-H1", StringComparison.Ordinal));
        });
        test("SYS03: custom coverage rejects gaps overlaps outside bounds and missing ends atomically", async f =>
        {
            var original = Request();
            foreach (var periods in new[] {
                new[] { original.Periods![0] with { StartsOn = new(2027, 1, 2) }, original.Periods[1] },
                new[] { original.Periods![0], original.Periods[1] with { StartsOn = new(2027, 7, 2) } },
                new[] { original.Periods![0], original.Periods[1] with { StartsOn = new(2027, 6, 30) } },
                new[] { original.Periods![0], original.Periods[1] with { EndsOn = new(2028, 1, 1) } },
                new[] { original.Periods![0] } })
                await Error("invalid_period_coverage", () => f.Service.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), original with { Periods = periods }, default));
            Ensure(f.Store.Count<FiscalYear>() == 1 && f.Store.Count<FiscalPeriod>() == 12);
        });
        test("SYS03: custom empty null entry reversed range and duplicate codes rejected", async f =>
        {
            await Error("invalid_period_count", () => Create(f, Request() with { Periods = Array.Empty<FiscalPeriodRequest>() }));
            await Error("invalid_period", () => Create(f, Request() with { Periods = new FiscalPeriodRequest[] { null! } }));
            await Error("invalid_period", () => Create(f, Request() with { Periods = new[] { new FiscalPeriodRequest("P", new(2027, 2, 1), new(2027, 1, 1)) } }));
            await Error("duplicate_period_code", () => Create(f, Request() with { Periods = new[] { Request().Periods![0], Request().Periods![1] with { Code = "2027-h1" } } }));
            await Error("duplicate_period_code", () => Create(f, Request() with { Periods = new[] { new FiscalPeriodRequest("2026-01", new(2027, 1, 1), new(2027, 12, 31)) } }));
        });
        test("SYS03: default monthly generation preserves leap day and maximum date", async f =>
        {
            var leap = await Create(f, new("2028", new(2028, 1, 1), new(2028, 12, 31)));
            var calendar = await f.Service.GetFiscalCalendar(f.CompanyId, leap.ResourceId, f.Owner, default);
            Ensure(calendar.Periods.Count == 12 && calendar.Periods[1].EndsOn == new DateOnly(2028, 2, 29));
            var last = await Create(f, new("9999", new(9999, 12, 31), new(9999, 12, 31), new[] { new FiscalPeriodRequest("LAST", new(9999, 12, 31), new(9999, 12, 31)) }));
            Ensure((await f.Service.GetFiscalCalendar(f.CompanyId, last.ResourceId, f.Owner, default)).Periods.Count == 1);
        });
        test("SYS03: custom period posting and GL close state match calendar and usage", async f =>
        {
            var r = await Create(f, Request()); var calendar = await f.Service.GetFiscalCalendar(f.CompanyId, r.ResourceId, f.Owner, default);
            var j = await f.Draft(f.Request() with { DocumentDate = new(2027, 7, 1), AccountingDate = new(2027, 7, 1) });
            Ensure((await f.Journal(j.ResourceId)).FiscalPeriodId == calendar.Periods[1].Id);
            Ensure((await f.Service.GetFiscalYearUsage(f.CompanyId, r.ResourceId, f.Owner, default)).UnpostedJournalCount == 1);
            await f.Post(j);
            foreach (var p in calendar.Periods)
                await f.Service.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(p.Version, "إقفال الأستاذ"), true, default);
            Ensure((await f.Service.GetFiscalCalendar(f.CompanyId, r.ResourceId, f.Owner, default)).State == FiscalCalendarState.Closed);
            var period = f.Store.All<FiscalPeriod>().Single(x => x.Id == calendar.Periods[0].Id);
            await f.Service.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "فتح الأستاذ"), false, default);
            Ensure((await f.Service.GetFiscalCalendar(f.CompanyId, r.ResourceId, f.Owner, default)).State == FiscalCalendarState.PartiallyClosed);
            Ensure((await f.Service.ListFiscalYearAudit(f.CompanyId, r.ResourceId, f.Owner, 0, 20, default)).TotalCount == 4);
        });
        test("SYS03: year cannot precede company fiscal start or overlap another year", async f =>
        {
            await Error("company_fiscal_start_mismatch", () => Create(f, new("2025", new(2025, 1, 1), new(2025, 12, 31))));
            await Error("fiscal_year_overlap", () => Create(f, new("OVERLAP", new(2026, 12, 31), new(2027, 12, 30))));
        });
        test("SYS03: usage and audit protect global scope and foreign company calendar", async f =>
        {
            var year = f.Store.All<FiscalYear>().Single();
            await Error("all_branches_required", () => f.Service.GetFiscalYearUsage(f.CompanyId, year.Id, f.Limited, default));
            await Error("permission_denied", () => f.Service.ListFiscalYearAudit(f.CompanyId, year.Id, f.Limited, 0, 10, default));
            await Error("reference_not_found", () => f.Service.GetFiscalCalendar(f.CompanyId, Guid.NewGuid(), f.Owner, default));
            await Error("permission_denied", () => f.Service.CreateFiscalYear(f.CompanyId, f.Limited, Fixture.Key(), Request(), default));
        });
        test("SYS03: failed calendar saves no periods audit or receipt and exact retry replays", async f =>
        {
            var audits = f.Store.Count<AuditEvent>(); var key = Fixture.Key(); f.Store.FailBeforeCommit = true;
            try { await f.Service.CreateFiscalYear(f.CompanyId, f.Owner, key, Request(), default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure(f.Store.Count<FiscalYear>() == 1 && f.Store.Count<FiscalPeriod>() == 12 && f.Store.Count<AuditEvent>() == audits);
            var r = await f.Service.CreateFiscalYear(f.CompanyId, f.Owner, key, Request(), default);
            Ensure(await f.Service.CreateFiscalYear(f.CompanyId, f.Owner, key, Request(), default) == r && f.Store.Count<FiscalPeriod>() == 14);
        });
        test("SYS03 GL04: custom period with multiple recurring dates is explicitly rejected", async f =>
        {
            var year = await Create(f, Request());
            var period = (await f.Service.GetFiscalCalendar(f.CompanyId, year.ResourceId, f.Owner, default)).Periods[0];
            var template = new RecurringJournalTemplate(f.CompanyId, f.BranchId, "شهري", RecurringFrequency.Monthly, RecurringGenerationMode.Manual,
                new(2027, 1, 1), new(2027, 12, 31), f.Clerk.Subject, DateTimeOffset.UtcNow);
            await Error("ambiguous_recurring_period", () => { template.DueDateFor(period.StartsOn, period.EndsOn); return Task.CompletedTask; });
        });
    }
    private static Task<CommandReceipt> Create(Fixture f, CreateFiscalYearRequest r) => f.Service.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), r, default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Calendar assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
