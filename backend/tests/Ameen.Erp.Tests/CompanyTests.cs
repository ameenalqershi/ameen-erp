using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CompanyTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS01: legal profile, Unicode address and leading zero registrations roundtrip", async f =>
        {
            var c = await Company(f); var profile = Profile(c) with { EnglishName = "Example company", Notes = "ملاحظات", CommercialRegistration = "000123",
                TaxRegistration = "000456", District = "حي الاختبار", BuildingNumber = "001", PostalCode = "00012", AdditionalNumber = "0034" };
            var r = await f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { Profile = profile }, default);
            c = await Company(f);
            Ensure(c.Version == r.Version && c.Revision == 2 && c.IsProfileComplete && c.CommercialRegistration == "000123" && c.PostalCode == "00012" &&
                c.EnglishName == "Example company" && c.Notes == "ملاحظات" && c.LegalName == profile.LegalName && c.IsActive);
            Ensure(c.FiscalYearStartsOn == new DateOnly(2026, 1, 1) && c.District == "حي الاختبار");
            Ensure((await f.Service.Company.ListCompanyAudit(f.CompanyId, f.Owner, 0, 10, default)).TotalCount == 2);
        });
        test("SYS01: required legal fields and profile reject null/empty without creating tenant", async f =>
        {
            foreach (var profile in new[] { Fixture.Profile() with { LegalName = " " }, Fixture.Profile() with { City = "" }, Fixture.Profile() with { Street = new string('x', 301) } })
                await Error("invalid_text", () => f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(Guid.NewGuid(), "INVALID", "غير صالح", "YE", "SAR", 2, profile), default));
            await Error("company_profile_required", () => f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(Guid.NewGuid(), "INVALID", "غير صالح", "YE", "SAR", 2, null!), default));
            Ensure(f.Store.Count<Company>() == 1);
        });
        test("SYS01: unused functional currency changes atomically with matching currency precision", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("JPY", 0, "ين"), default);
            var c = await Company(f); await f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { FunctionalCurrency = "JPY" }, default);
            c = await Company(f); Ensure(c.FunctionalCurrency == "JPY" && c.CurrencyScale == 0);
            var request = f.Request() with { Lines = f.Request().Lines.Select(x => x with { CurrencyCode = "JPY" }).ToArray() };
            await f.Post(await f.Draft(request));
            var report = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default);
            Ensure(report.Currency == "JPY" && report.TotalClosingDebit == 100);
        });
        test("SYS01: even cancelled journal prevents functional currency conversion", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            await f.Action(await f.Draft(), "cancel", reason: "إلغاء"); var c = await Company(f);
            var auditBefore = f.Store.Count<AuditEvent>();
            await Error("functional_currency_in_use", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { FunctionalCurrency = "USD" }, default));
            var rejected = await Company(f);
            Ensure(rejected.FunctionalCurrency == c.FunctionalCurrency &&
                rejected.CurrencyScale == c.CurrencyScale &&
                rejected.Revision == c.Revision &&
                rejected.Version == c.Version &&
                f.Store.Count<AuditEvent>() == auditBefore);
            Ensure((await f.Service.Company.GetCompanyUsage(f.CompanyId, f.Owner, default)).CanChangeFunctionalCurrency == false);
        });
        test("SYS01: exchange-rate history blocks base-currency change before any journal", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", Fixture.Date, 3.75m, "اختبار"), default);
            var c = await Company(f);
            await Error("functional_currency_in_use", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { FunctionalCurrency = "USD" }, default));
        });
        test("SYS01: recurring template alone prevents functional conversion", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Clerk.Subject, Permissions.All, true, Array.Empty<Guid>(), member.Version), default);
            await f.Service.RecurringJournal.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), new(f.BranchId, "دوري", RecurringFrequency.Monthly, RecurringGenerationMode.Manual,
                Fixture.Date, Fixture.Date, f.Request().Lines.Select(x => new RecurringLineRequest(x.AccountId, null, x.Description, x.CurrencyCode, x.Debit, x.Credit)).ToArray()), default);
            var c = await Company(f);
            await Error("functional_currency_in_use", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { FunctionalCurrency = "USD" }, default));
        });
        test("SYS01: inactive currency cannot become functional", async f =>
        {
            var usd = await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            await f.Service.Currency.SetCurrencyStatus(f.CompanyId, usd.ResourceId, f.Owner, Fixture.Key(), new(usd.Version, false, "تعطيل"), default);
            var c = await Company(f); await Error("currency_inactive", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { FunctionalCurrency = "USD" }, default));
        });
        test("SYS01 SYS03: first fiscal start must match profile and existing calendar cannot be shifted", async f =>
        {
            var cid = Guid.NewGuid(); await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(cid, "NEW", "جديدة", "YE", "SAR", 2, Fixture.Profile()), default);
            await Error("company_fiscal_start_mismatch", () => f.Service.FiscalCalendar.CreateFiscalYear(cid, f.Owner, Fixture.Key(), new("2027", new(2027, 1, 1), new(2027, 12, 31)), default));
            var c = await Company(f);
            await Error("company_calendar_in_use", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { Profile = Profile(c) with { FiscalYearStartsOn = new(2026, 2, 1) } }, default));
        });
        test("SYS01: inactive company blocks setup, financial writes and idempotent replay while reads remain", async f =>
        {
            var key = Fixture.Key(); var draft = await f.Draft(key: key); var c = await Company(f);
            var off = await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, Fixture.Key(), new(c.Version, false, "إيقاف التشغيل"), default);
            await Error("company_inactive", () => f.Draft(key: key));
            await Error("company_inactive", () => f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("OFF", "معطل", "مدينة الاختبار", "عنوان الاختبار"), default));
            await Error("company_inactive", () => f.Action(draft, "submit"));
            Ensure((await f.Service.Journal.GetJournal(f.CompanyId, draft.ResourceId, f.Clerk, default)).State == JournalState.Draft);
            Ensure((await f.Service.Company.ListCompanies(f.Clerk, default)).Single().IsActive == false);
            await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, Fixture.Key(), new(off.Version, true, "إعادة التشغيل"), default);
            Ensure(await f.Draft(key: key) == draft); await f.Action(draft, "submit");
        });
        test("SYS01: inactive company can correct its profile without enabling business commands", async f =>
        {
            var c = await Company(f); await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, Fixture.Key(), new(c.Version, false, "إيقاف"), default);
            c = await Company(f); await f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { Name = "شركة معدلة" }, default);
            Ensure(!(await Company(f)).IsActive);
            await Error("company_inactive", () => f.Draft());
        });
        test("SYS01: duplicate company code and cross-company authority do not bypass membership", async f =>
        {
            var cid = Guid.NewGuid(); await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            var c = await Company(f); await Error("company_exists", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), Edit(c) with { Code = "OTHER" }, default));
            await Error("permission_denied", () => f.Service.Company.EditCompany(f.CompanyId, new Actor("outsider", true), Fixture.Key(), Edit(c), default));
            await Error("all_branches_required", () => f.Service.Company.GetCompanyUsage(f.CompanyId, f.Limited, default));
        });
        test("SYS01-OP02: status save rejects unauthorized and stale edits without audit", async f =>
        {
            var before = await Company(f);
            var initialAuditCount = f.Store.Count<AuditEvent>();
            await Error("permission_denied", () => f.Service.Company.SetCompanyStatus(
                f.CompanyId, f.Clerk, Fixture.Key(), new(before.Version, false, "رفض صلاحية"), default));
            Ensure((await Company(f)).IsActive && f.Store.Count<AuditEvent>() == initialAuditCount);
            var key = Fixture.Key();
            var request = new SetCompanyStatusRequest(before.Version, false, "تعطيل معتمد");
            var disabled = await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, key, request, default);
            Ensure(await f.Service.Company.SetCompanyStatus(f.CompanyId, f.Owner, key, request, default) == disabled);
            await Error("version_conflict", () => f.Service.Company.SetCompanyStatus(
                f.CompanyId, f.Owner, Fixture.Key(), new(before.Version, true, "إصدار قديم"), default));
            var persisted = await Company(f);
            Ensure(!persisted.IsActive && persisted.Version == disabled.Version &&
                persisted.Revision == before.Revision + 1 &&
                f.Store.Count<AuditEvent>() == initialAuditCount + 1);
            var restored = await f.Service.Company.SetCompanyStatus(
                f.CompanyId, f.Owner, Fixture.Key(), new(disabled.Version, true, "إعادة تفعيل"), default);
            Ensure((await Company(f)).IsActive && (await Company(f)).Version == restored.Version &&
                f.Store.Count<AuditEvent>() == initialAuditCount + 2);
        });
        test("SYS01: metadata edit is replayable, stale-protected and rollback-safe", async f =>
        {
            var c = await Company(f); var request = Edit(c) with { Profile = Profile(c) with { Notes = "تعديل" } }; var key = Fixture.Key();
            var auditCount = f.Store.Count<AuditEvent>(); f.Store.FailBeforeCommit = true;
            try { await f.Service.Company.EditCompany(f.CompanyId, f.Owner, key, request, default); throw new Exception("Expected rollback"); } catch (IOException) { }
            Ensure((await Company(f)).Revision == 1 && (await Company(f)).Version == request.ExpectedVersion && f.Store.Count<AuditEvent>() == auditCount);
            var result = await f.Service.Company.EditCompany(f.CompanyId, f.Owner, key, request, default);
            Ensure(await f.Service.Company.EditCompany(f.CompanyId, f.Owner, key, request, default) == result);
            await Error("version_conflict", () => f.Service.Company.EditCompany(f.CompanyId, f.Owner, Fixture.Key(), request, default));
        });
    }
    public static CompanyProfile Profile(Company c) => new(c.LegalName!, c.Jurisdiction!, c.FiscalYearStartsOn!.Value, c.City!, c.Street!,
        c.EnglishName, c.Notes, c.CommercialRegistration, c.TaxRegistration, c.District, c.BuildingNumber, c.PostalCode, c.AdditionalNumber);
    public static EditCompanyRequest Edit(Company c) => new(c.Version, c.Code, c.Name, c.CountryCode, c.FunctionalCurrency, Profile(c), "تحديث بطاقة الشركة");
    private static Task<Company> Company(Fixture f) => f.Service.Company.GetCompany(f.CompanyId, f.Owner, default);
    private static void Ensure(bool condition) { if (!condition) throw new Exception("Company assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
