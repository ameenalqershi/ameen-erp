using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class RateAndCloseTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS05: quote selects latest effective date, fixes direction and preserves exact rate", async f =>
        {
            await Currency(f);
            var old = await Rate(f, Fixture.Date.AddDays(-10), 3.123456789m);
            var current = await Rate(f, Fixture.Date, 3.75m); await Rate(f, Fixture.Date.AddDays(10), 4m);
            var quote = await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Limited, "usd", Fixture.Date.AddDays(-1), default);
            Ensure(quote.ExchangeRateId == old.ResourceId && quote.RateToFunctional == 3.123456789m && quote.FunctionalCurrency == "SAR" && quote.CurrencyCode == "USD");
            Ensure((await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default)).ExchangeRateId == current.ResourceId);
        });
        test("SYS05: functional quote is one without fabricated rate and missing historical quote fails", async f =>
        {
            var own = await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "SAR", Fixture.Date, default);
            Ensure(own.IsFunctionalCurrency && own.ExchangeRateId is null && own.EffectiveDate is null && own.RateToFunctional == 1m);
            await Currency(f); await Rate(f, Fixture.Date.AddDays(1), 4m);
            await Error("exchange_rate_required", () => f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default));
        });
        test("SYS05: historical search and detail remain readable for disabled currency while quote rejects it", async f =>
        {
            var currency = await Currency(f); var rate = await Rate(f, Fixture.Date, 3.75m);
            await f.Service.Currency.SetCurrencyStatus(f.CompanyId, currency.ResourceId, f.Owner, Fixture.Key(), new(currency.Version, false, "تعطيل"), default);
            await Error("currency_inactive", () => f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default));
            Ensure((await f.Service.ExchangeRate.GetExchangeRate(f.CompanyId, rate.ResourceId, f.Clerk, default)).Rate.RateToFunctional == 3.75m);
            Ensure((await f.Service.ExchangeRate.SearchExchangeRates(f.CompanyId, f.Clerk, "USD", Fixture.Date, Fixture.Date, "مصدر", 0, 10, default)).TotalCount == 1);
        });
        test("SYS05: history filters pagination and creation audit retain source", async f =>
        {
            await Currency(f); var first = await Rate(f, Fixture.Date.AddDays(-1), 3m); await Rate(f, Fixture.Date, 4m);
            var page = await f.Service.ExchangeRate.SearchExchangeRates(f.CompanyId, f.Clerk, "USD", null, Fixture.Date, "مصدر", 1, 1, default);
            Ensure(page.TotalCount == 2 && page.Items[0].Id == first.ResourceId);
            var audit = await f.Service.ExchangeRate.ListExchangeRateAudit(f.CompanyId, first.ResourceId, f.Owner, 0, 10, default);
            Ensure(audit.TotalCount == 1 && audit.Items[0].Details.Contains("source", StringComparison.Ordinal));
            await Error("invalid_date_range", () => f.Service.ExchangeRate.SearchExchangeRates(f.CompanyId, f.Clerk, null, Fixture.Date.AddDays(1), Fixture.Date, null, 0, 10, default));
        });
        test("SYS05: usage includes cancelled and reversal journals once per document", async f =>
        {
            await Currency(f); var rate = await Rate(f, Fixture.Date, 3.75m); var request = Request(f, rate.ResourceId);
            var cancelled = await f.Draft(request); await f.Action(cancelled, "cancel", reason: "إلغاء");
            var posted = await f.Post(await f.Draft(request));
            await f.Service.Journal.ReverseJournal(f.CompanyId, posted.ResourceId, f.Clerk, Fixture.Key(), new(posted.Version, Fixture.Date, "عكس"), default);
            var usage = await f.Service.ExchangeRate.GetExchangeRateUsage(f.CompanyId, rate.ResourceId, f.Owner, 0, 2, default);
            Ensure(usage.TotalCount == 3 && usage.Items.Count == 2);
            await Error("all_branches_required", () => f.Service.ExchangeRate.GetExchangeRateUsage(f.CompanyId, rate.ResourceId, f.Limited, 0, 10, default));
            await Error("permission_denied", () => f.Service.ExchangeRate.ListExchangeRateAudit(f.CompanyId, rate.ResourceId, f.Clerk, 0, 10, default));
        });
        test("SYS05: quote is advisory and newer rate requires refreshed draft choice", async f =>
        {
            await Currency(f); await Rate(f, Fixture.Date.AddDays(-1), 3m);
            var quote = await f.Service.ExchangeRate.QuoteExchangeRate(f.CompanyId, f.Clerk, "USD", Fixture.Date, default);
            var newer = await Rate(f, Fixture.Date, 3.75m);
            await Error("stale_exchange_rate", () => f.Draft(Request(f, quote.ExchangeRateId!.Value)));
            await f.Draft(Request(f, newer.ResourceId));
        });
        test("SYS05: detail and quote cannot read another company", async f =>
        {
            var cid = Guid.NewGuid(); await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            await f.Service.Currency.CreateCurrency(cid, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            var r = await f.Service.ExchangeRate.CreateExchangeRate(cid, f.Owner, Fixture.Key(), new("USD", Fixture.Date, 4m, "مصدر"), default);
            await Error("reference_not_found", () => f.Service.ExchangeRate.GetExchangeRate(f.CompanyId, r.ResourceId, f.Owner, default));
            await Error("permission_denied", () => f.Service.ExchangeRate.QuoteExchangeRate(cid, f.Clerk, "USD", Fixture.Date, default));
        });
        test("GL11: preview lists pending journals with accurate paginated blockers without writes", async f =>
        {
            await f.Draft(); await f.Draft(); var audit = f.Store.Count<AuditEvent>(); var period = Period(f);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, period.Id, f.Approver, 1, 1, default);
            Ensure(!preview.CanCloseGeneralLedger && preview.GeneralLedgerOnly && preview.Issues.TotalCount == 2 && preview.Issues.Items.Count == 1 && preview.ExpectedVersion == period.Version);
            Ensure(f.Store.Count<AuditEvent>() == audit && !Period(f).IsClosed);
            await Error("unposted_journals", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default));
        });
        test("GL11: posted/cancelled documents pass preview and close then closed preview blocks", async f =>
        {
            await f.Post(); await f.Action(await f.Draft(), "cancel", reason: "إلغاء"); var p = Period(f);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Approver, 0, 10, default);
            Ensure(preview.CanCloseGeneralLedger && preview.PostedJournalCount == 1 && preview.UnpostedJournalCount == 0 && preview.Issues.TotalCount == 0);
            await f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(preview.ExpectedVersion, "إقفال"), true, default);
            Ensure(!(await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Approver, 0, 10, default)).CanCloseGeneralLedger);
        });
        test("GL11: preview does not reserve period and close rechecks newly created drafts", async f =>
        {
            var p = Period(f); var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Approver, 0, 10, default);
            Ensure(preview.CanCloseGeneralLedger); await f.Draft();
            await Error("unposted_journals", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(preview.ExpectedVersion, "إقفال"), true, default));
        });
        test("GL11: preview requires close permission and all-branch read scope", async f =>
        {
            var p = Period(f);
            await Error("permission_denied", () => f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Owner, 0, 10, default));
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Limited.Subject, new[] { Permissions.Read, Permissions.PeriodClose }, false, new[] { f.BranchId }, member.Version), default);
            await Error("all_branches_required", () => f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, p.Id, f.Limited, 0, 10, default));
        });
        test("GL11: inactive company fails preview operational checks without hiding financial history", async f =>
        {
            var company = await f.Service.Company.GetCompany(f.CompanyId, f.Owner, default);
            await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, Fixture.Key(), new(company.Version, false, "إيقاف"), default);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, Period(f).Id, f.Approver, 0, 10, default);
            Ensure(!preview.CanCloseGeneralLedger && !preview.Checks.Single(x => x.Code == "company_active").Passed);
        });
    }
    private static FiscalPeriod Period(Fixture f) => f.Store.All<FiscalPeriod>().Single(x => x.StartsOn <= Fixture.Date && x.EndsOn >= Fixture.Date);
    private static Task<CommandReceipt> Currency(Fixture f) => f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
    private static Task<CommandReceipt> Rate(Fixture f, DateOnly date, decimal rate) => f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", date, rate, "مصدر الاختبار"), default);
    private static CreateJournalRequest Request(Fixture f, Guid rate)
    {
        var r = f.Request(100, 375); r.Lines[0] = r.Lines[0] with { CurrencyCode = "USD", ExchangeRateId = rate }; return r;
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Rate/close assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
