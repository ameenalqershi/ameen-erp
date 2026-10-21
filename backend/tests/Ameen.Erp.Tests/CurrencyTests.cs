using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CurrencyTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS04: currency labels, symbol, fraction, revision and active search roundtrip", async f =>
        {
            var r = await Create(f); var c = await f.Service.GetCurrency(f.CompanyId, r.ResourceId, f.Clerk, default);
            Ensure(c.Name == "دولار أمريكي" && c.EnglishName == "US dollar" && c.DisplaySymbol == "$" && c.FractionName == "سنت" && c.Revision == 1);
            Ensure((await f.Service.SearchCurrencies(f.CompanyId, f.Clerk, "dollar", true, 0, 10, default)).Items.Single().Id == c.Id);
            await f.Service.SetCurrencyStatus(f.CompanyId, c.Id, f.Owner, Fixture.Key(), new(c.Version, false, "تعطيل"), default);
            Ensure((await f.Service.SearchCurrencies(f.CompanyId, f.Clerk, "USD", true, 0, 10, default)).TotalCount == 0);
        });
        test("SYS04: unused foreign precision can change with optimistic version and replay", async f =>
        {
            var r = await Create(f); var c = await Currency(f, r); var key = Fixture.Key(); var request = Edit(c) with { Scale = 3 };
            var changed = await f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, key, request, default);
            Ensure(await f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, key, request, default) == changed);
            Ensure((await Currency(f, changed)).Scale == 3 && (await Currency(f, changed)).Revision == 2);
            await Error("version_conflict", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, Fixture.Key(), request, default));
            await Error("idempotency_conflict", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, key, request with { Scale = 4 }, default));
        });
        test("SYS04: functional currency cannot be disabled or precision-diverged from its company", async f =>
        {
            var c = f.Store.All<Currency>().Single();
            await Error("functional_currency_required", () => f.Service.SetCurrencyStatus(f.CompanyId, c.Id, f.Owner, Fixture.Key(), new(c.Version, false, "تعطيل"), default));
            await Error("functional_currency_scale_fixed", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { Scale = 3 }, default));
            var log = await f.Service.ListCurrencyAudit(f.CompanyId, c.Id, f.Owner, 0, 10, default);
            Ensure(log.Items.Single().Action == "currency.created");
        });
        test("SYS04: account reference freezes currency precision before first journal", async f =>
        {
            var r = await Create(f); var c = await Currency(f, r);
            await f.Service.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("USD-A", "حساب الدولار", AccountType.Asset, null, true, false, false, "USD", NormalBalance.Debit), default);
            await Error("currency_precision_in_use", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { Scale = 3 }, default));
            Ensure((await f.Service.GetCurrencyUsage(f.CompanyId, c.Id, f.Owner, default)).AccountCount == 1);
        });
        test("SYS04: historical exchange rate freezes precision without any journal", async f =>
        {
            var r = await Create(f); var c = await Currency(f, r);
            await Rate(f);
            await Error("currency_precision_in_use", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { Scale = 3 }, default));
        });
        test("SYS04: recurring template locks precision even before any exchange rate exists", async f =>
        {
            var r = await Create(f); var c = await Currency(f, r);
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject);
            await f.Service.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Clerk.Subject, Permissions.All, true, Array.Empty<Guid>(), member.Version), default);
            await f.Service.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), new(f.BranchId, "دولار دوري", RecurringFrequency.Monthly,
                RecurringGenerationMode.Manual, Fixture.Date, Fixture.Date,
                f.Request().Lines.Select(x => new RecurringLineRequest(x.AccountId, null, x.Description, "USD", x.Debit, x.Credit)).ToArray()), default);
            await Error("currency_precision_in_use", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { Scale = 3 }, default));
            var usage = await f.Service.GetCurrencyUsage(f.CompanyId, c.Id, f.Owner, default);
            Ensure(usage.RecurringTemplateCount == 1 && usage.ExchangeRateCount == 0);
        });
        test("SYS04: disabled currency blocks new rates, accounts and journal lines", async f =>
        {
            var r = await Create(f); var rate = await Rate(f);
            await f.Service.SetCurrencyStatus(f.CompanyId, r.ResourceId, f.Owner, Fixture.Key(), new(r.Version, false, "إيقاف"), default);
            await Error("currency_inactive", () => Rate(f, Fixture.Date.AddDays(1)));
            await Error("currency_inactive", () => f.Service.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("USD-A", "دولار", AccountType.Asset, null, true, false, false, "USD", NormalBalance.Debit), default));
            await Error("currency_inactive", () => f.Draft(Request(f, rate.ResourceId)));
        });
        test("SYS04: disable after approval blocks posting; reactivation preserves exact amounts", async f =>
        {
            var r = await Create(f); var rate = await Rate(f); var draft = await f.Draft(Request(f, rate.ResourceId));
            var approved = await f.Action(await f.Action(draft, "submit"), "approve", f.Approver);
            var off = await f.Service.SetCurrencyStatus(f.CompanyId, r.ResourceId, f.Owner, Fixture.Key(), new(r.Version, false, "إيقاف"), default);
            await Error("currency_inactive", () => f.Action(approved, "post"));
            Ensure((await f.Journal(approved.ResourceId)).State == JournalState.Approved && f.Store.Count<OutboxEvent>() == 0);
            await f.Service.SetCurrencyStatus(f.CompanyId, r.ResourceId, f.Owner, Fixture.Key(), new(off.Version, true, "تفعيل"), default);
            await f.Action(approved, "post");
            var journal = await f.Journal(approved.ResourceId);
            Ensure(journal.Lines.Sum(x => x.FunctionalDebit) == 375 && journal.Lines[0].ExchangeRate == 3.75m);
        });
        test("SYS04: labels remain editable after use and disabled currency does not hide posted history", async f =>
        {
            var r = await Create(f); var rate = await Rate(f); await f.Post(await f.Draft(Request(f, rate.ResourceId)));
            var c = await Currency(f, r);
            var edit = await f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { Name = "دولار", Notes = "مستخدم" }, default);
            await f.Service.SetCurrencyStatus(f.CompanyId, c.Id, f.Owner, Fixture.Key(), new(edit.Version, false, "إيقاف"), default);
            var trial = await f.Service.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default);
            Ensure(trial.TotalClosingDebit == 375 && trial.TotalClosingCredit == 375);
            Ensure((await f.Service.GetCurrencyUsage(f.CompanyId, c.Id, f.Owner, default)).JournalCount == 1);
        });
        test("SYS04: scoped reader cannot read global usage and foreign-company currency is rejected", async f =>
        {
            var r = await Create(f); var c = await Currency(f, r);
            await Error("all_branches_required", () => f.Service.GetCurrencyUsage(f.CompanyId, c.Id, f.Limited, default));
            await Error("permission_denied", () => f.Service.EditCurrency(f.CompanyId, c.Id, f.Clerk, Fixture.Key(), Edit(c), default));
            var other = Guid.NewGuid(); await f.Service.CreateCompany(f.Owner, Fixture.Key(), new(other, "OTHER", "أخرى", "YE", "YER", 2, Fixture.Profile()), default);
            await Error("reference_not_found", () => f.Service.GetCurrency(other, c.Id, f.Owner, default));
        });
        test("SYS04: failed metadata edit rolls back card, revision, audit and receipt", async f =>
        {
            var r = await Create(f); var c = await Currency(f, r); var request = Edit(c) with { Notes = "جديد" }; var count = f.Store.Count<AuditEvent>(); var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, key, request, default); throw new Exception("Expected failure"); }
            catch (IOException) { }
            c = await Currency(f, r); Ensure(c.Notes is null && c.Revision == 1 && c.Version == r.Version && f.Store.Count<AuditEvent>() == count);
            await f.Service.EditCurrency(f.CompanyId, c.Id, f.Owner, key, request, default);
        });
        test("SYS04: duplicate ISO, invalid scale and overlong symbol are rejected", async f =>
        {
            await Create(f); await Error("duplicate_currency", () => Create(f));
            await Error("invalid_scale", () => f.Service.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("EUR", 7, "يورو"), default));
            await Error("invalid_text", () => f.Service.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("EUR", 2, "يورو", DisplaySymbol: new string('x', 21)), default));
        });
    }
    public static EditCurrencyRequest Edit(Currency c) => new(c.Version, c.Scale, c.Name, c.EnglishName, c.Notes, c.DisplaySymbol, c.FractionName, "تحديث البطاقة");
    private static Task<CommandReceipt> Create(Fixture f) => f.Service.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار أمريكي", "US dollar", null, "$", "سنت"), default);
    private static Task<Currency> Currency(Fixture f, CommandReceipt r) => f.Service.GetCurrency(f.CompanyId, r.ResourceId, f.Owner, default);
    private static Task<CommandReceipt> Rate(Fixture f, DateOnly? date = null) => f.Service.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", date ?? Fixture.Date, 3.75m, "اختبار"), default);
    private static CreateJournalRequest Request(Fixture f, Guid rate) => f.Request(100, 375) with
    { Lines = new[] { f.Request().Lines[0] with { CurrencyCode = "USD", ExchangeRateId = rate }, f.Request(100, 375).Lines[1] } };
    private static void Ensure(bool condition) { if (!condition) throw new Exception("Currency assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
