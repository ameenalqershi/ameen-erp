using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class GeneralOpeningTests
{
    internal static CreateGeneralOpeningRequest Request(CreateJournalRequest r) => new(r.BranchId, r.DocumentDate, r.AccountingDate,
        r.AccountingDate, "TB-2026", "SAR", 1, r.Description, r.ExternalReference, r.Lines);
    private static Task<CommandReceipt> Create(Fixture f, CreateGeneralOpeningRequest? r = null, string? key = null) =>
        f.Service.Journal.CreateGeneralOpening(f.CompanyId, f.Clerk, key ?? Fixture.Key(), r ?? Request(f.Request()), default);
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL02-A01: search by document, description and references respects pagination and branch scope", async f =>
        {
            var first = await Create(f, Request(f.Request()) with {
                PreviousTrialBalanceReference = "TB-OPEN-ALPHA", ExternalReference = "EXT-ALPHA", Description = "Opening alpha" });
            var other = await Create(f, Request(f.Request(branch: f.OtherBranchId)) with {
                PreviousTrialBalanceReference = "TB-OPEN-BETA", ExternalReference = "EXT-BETA", Description = "Opening beta" });
            var entry = await f.Journal(first.ResourceId);
            var number = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.GeneralOpening, entry.Number);
            Ensure(number.TotalCount == 1 && number.Items[0].Id == first.ResourceId);
            var reference = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.GeneralOpening, "TB-OPEN-BETA");
            Ensure(reference.TotalCount == 1 && reference.Items[0].Id == other.ResourceId);
            var description = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.GeneralOpening, "Opening alpha");
            Ensure(description.TotalCount == 1 && description.Items[0].Id == first.ResourceId);
            var empty = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.GeneralOpening, "absent-reference");
            Ensure(empty.TotalCount == 0);
            var scoped = await f.Service.Journal.ListJournals(f.CompanyId, f.Limited, null, null, 0, 10, default,
                JournalSource.GeneralOpening, "EXT-BETA");
            Ensure(scoped.TotalCount == 0);
            await Error("search_too_long", () => f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10,
                default, JournalSource.GeneralOpening, new string('x', 101)));
        });
        test("GL02: opening metadata and independent approval posting use one balanced ledger", async f =>
        {
            var r = await Create(f); var source = await f.Journal(r.ResourceId);
            Ensure(source.Source == JournalSource.GeneralOpening && source.OpeningRevision == 1 && source.PreviousTrialBalanceReference == "TB-2026");
            r = await f.Action(r, "submit"); await Error("self_approval_forbidden", () => f.Action(r, "approve"));
            r = await f.Action(r, "approve", f.Approver); r = await f.Action(r, "post");
            source = await f.Journal(r.ResourceId);
            Ensure(source.OpeningRevision == 4 && source.State == JournalState.Posted && f.Store.Count<JournalEntry>() == 1 && f.Store.Count<OutboxEvent>() == 1);
            Ensure((await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default, JournalSource.GeneralOpening)).TotalCount == 1);
        });
        test("GL02: control accounts cannot duplicate subsidiary openings", async f =>
        {
            var control = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("CTRL", "رقابة", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default);
            var request = Request(f.Request()); request.Lines[0] = request.Lines[0] with { AccountId = control.ResourceId };
            await Error("control_account_protected", () => Create(f, request)); Ensure(f.Store.Count<JournalEntry>() == 0);
        });
        test("GL02: missing prior balance reference and foreign header or lines fail atomically", async f =>
        {
            await Error("invalid_text", () => Create(f, Request(f.Request()) with { PreviousTrialBalanceReference = " " }));
            await Error("currency_not_found", () => Create(f, Request(f.Request()) with { CurrencyCode = "USD" }));
            await Error("opening_functional_currency_only", () => Create(f, Request(f.Request()) with { ExchangeRate = 2 }));
            var request = Request(f.Request()); request.Lines[0] = request.Lines[0] with { CurrencyCode = "USD" };
            await Error("opening_currency_mismatch", () => Create(f, request)); Ensure(f.Store.Count<JournalEntry>() == 0);
        });
        test("GL02: draft correction changes prior reference and lines but ordinary edit cannot bypass currency policy", async f =>
        {
            var r = await Create(f);
            r = await f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "تصحيح", "TB-CORRECT", f.Request(125,125).Lines), default);
            var source = await f.Journal(r.ResourceId); Ensure(source.PreviousTrialBalanceReference == "TB-CORRECT" && source.OpeningRevision == 2 && source.Lines.Sum(x => x.Debit) == 125);
            var lines = f.Request().Lines; lines[0] = lines[0] with { CurrencyCode = "USD" };
            await Error("opening_currency_mismatch", () => f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "تجاوز", lines), default));
        });
        test("GL02: replay and failed save preserve one opening and immutable posted reference", async f =>
        {
            var key = Fixture.Key(); f.Store.FailBeforeCommit = true;
            try { await Create(f, key: key); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure(f.Store.Count<JournalEntry>() == 0);
            var r = await Create(f, key: key); Ensure(r == await Create(f, key: key));
            r = await f.Post(r);
            await Error("journal_state_conflict", () => f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "تعديل", "TB", f.Request().Lines), default));
        });
        test("GL02: historical reversal retains opening provenance and nets general ledger", async f =>
        {
            var original = await f.Post(await Create(f));
            var reversal = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "تصحيح"), default);
            await f.Post(reversal);
            var source = await f.Journal(original.ResourceId); Ensure(source.Source == JournalSource.GeneralOpening && source.State == JournalState.Reversed && source.PreviousTrialBalanceReference == "TB-2026");
            Ensure(f.Store.All<JournalEntry>().SelectMany(x => x.Lines).Where(x => x.AccountId == f.CashId).Sum(x => x.FunctionalDebit - x.FunctionalCredit) == 0);
        });
        test("GL02-GP01: foreign opening takes exact effective snapshot, rejects stale or mixed rates and reverses without repricing", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            var old = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(),
                new("USD", Fixture.Date.AddDays(-5), 3.25m, "سعر سابق"), default);
            var current = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(),
                new("USD", Fixture.Date, 3.75m, "سعر حالي"), default);
            var future = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(),
                new("USD", Fixture.Date.AddDays(5), 4m, "سعر لاحق"), default);
            var basic = Request(f.Request());
            var lines = basic.Lines.Select((x,i) => x with {
                CurrencyCode = "USD", ExchangeRateId = current.ResourceId,
                Debit = i == 0 ? 10m : 0m, Credit = i == 1 ? 10m : 0m }).ToArray();
            var request = basic with { CurrencyCode = "USD", ExchangeRate = 3.75m,
                PreviousTrialBalanceReference = "TB-USD-HIST", Lines = lines };
            await Error("opening_rate_mismatch", () => Create(f, request with { ExchangeRate = 3.5m }));
            await Error("exchange_rate_required", () => Create(f, request with {
                AccountingDate = Fixture.Date.AddDays(-15) }));
            await Error("stale_exchange_rate", () => Create(f, request with { Lines = lines.Select(x =>
                x with { ExchangeRateId = old.ResourceId }).ToArray() }));
            await Error("exchange_rate_mismatch", () => Create(f, request with { Lines = lines.Select(x =>
                x with { ExchangeRateId = future.ResourceId }).ToArray() }));
            await Error("exchange_rate_required", () => Create(f, request with { Lines = lines.Select(x =>
                x with { ExchangeRateId = null }).ToArray() }));
            await Error("opening_currency_mismatch", () => Create(f, request with { Lines = basic.Lines }));
            Ensure(f.Store.Count<JournalEntry>() == 0 && f.Store.Count<NumberSequence>() == 0);

            var created = await Create(f, request);
            var journal = await f.Journal(created.ResourceId);
            Ensure(journal.OpeningCurrencyCode == "USD" && journal.OpeningExchangeRate == 3.75m &&
                journal.Lines.All(x => x.ExchangeRateId == current.ResourceId && x.ExchangeRate == 3.75m) &&
                journal.Lines.Sum(x => x.FunctionalDebit) == 37.5m);
            var changedLines = lines.Select(x => x with { Debit = x.Debit * 2, Credit = x.Credit * 2 }).ToArray();
            created = await f.Service.Journal.EditGeneralOpening(f.CompanyId, created.ResourceId, f.Clerk,
                Fixture.Key(), new(created.Version, "تصحيح افتتاح أجنبي", "TB-USD-CORRECT", changedLines), default);
            await Error("opening_rate_mismatch", () => f.Service.Journal.EditGeneralOpening(f.CompanyId, created.ResourceId,
                f.Clerk, Fixture.Key(), new(created.Version, "سعر غير متطابق", "TB", changedLines.Select(x =>
                    x with { ExchangeRateId = old.ResourceId }).ToArray()), default));
            var posted = await f.Post(created);
            journal = await f.Journal(posted.ResourceId);
            Ensure(journal.State == JournalState.Posted && journal.Lines.Sum(x => x.FunctionalDebit) == 75m &&
                journal.OpeningCurrencyCode == "USD" && journal.OpeningExchangeRate == 3.75m);
            var reversed = await f.Service.Journal.ReverseJournal(f.CompanyId, posted.ResourceId, f.Clerk,
                Fixture.Key(), new(posted.Version, Fixture.Date, "عكس مبلغ الافتتاح الأجنبي"), default);
            await f.Post(reversed);
            var originalLines = (await f.Journal(posted.ResourceId)).Lines.OrderBy(x => x.Position).ToArray();
            var reversalLines = (await f.Journal(reversed.ResourceId)).Lines.OrderBy(x => x.Position).ToArray();
            Ensure(originalLines.Zip(reversalLines).All(pair => pair.First.ExchangeRate == pair.Second.ExchangeRate &&
                pair.First.ExchangeRateId == pair.Second.ExchangeRateId &&
                pair.First.FunctionalDebit == pair.Second.FunctionalCredit &&
                pair.First.FunctionalCredit == pair.Second.FunctionalDebit));
            Ensure(f.Store.All<JournalEntry>().SelectMany(x => x.Lines).Sum(x => x.FunctionalDebit - x.FunctionalCredit) == 0);
        });
        test("GL02: settlement preview uses historical clearing accounts and never adds posted candidate twice", async f =>
        {
            var (_, stock) = await InventoryOpeningPostingTests.Setup(f);
            await InventoryOpeningPostingTests.Post(f, stock, await InventoryOpeningPostingTests.Plan(f, stock));
            var request = Request(f.Request(10.5m,10.5m));
            request.Lines[0] = request.Lines[0] with { AccountId = f.EquityId }; request.Lines[1] = request.Lines[1] with { AccountId = f.CashId };
            var r = await Create(f, request);
            var settings = await f.Service.InventoryOpening.GetPostingSettings(f.CompanyId, f.Owner, default);
            await f.Service.InventoryOpening.ConfigurePosting(f.CompanyId, f.Owner, Fixture.Key(), new(settings!.Version, f.CashId, "تغيير"), default);
            var before = await f.Service.Journal.OpeningSettlement(f.CompanyId, r.ResourceId, f.Clerk, default);
            Ensure(before.Accounts.Single().AccountId == f.EquityId && before.Accounts.Single().PostedBalance == -10.5m && before.AllShownAccountsZero && !before.AllSubledgersImplemented);
            r = await f.Post(r); var after = await f.Service.Journal.OpeningSettlement(f.CompanyId, r.ResourceId, f.Clerk, default);
            Ensure(!after.IncludesUnpostedCandidate && after.Accounts.Single().PostedBalance == 0 && after.Accounts.Single().ProposedChange == 0);
        });
        test("GL02: pending opening blocks period close and cannot be accessed outside branch", async f =>
        {
            var r = await Create(f, Request(f.Request(branch: f.OtherBranchId)));
            await Error("branch_access_denied", () => f.Service.Journal.GetGeneralOpening(f.CompanyId, r.ResourceId, f.Limited, default));
            var source = await f.Journal(r.ResourceId);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, source.FiscalPeriodId, f.Approver, 0, 10, default);
            Ensure(!preview.CanCloseGeneralLedger);
            var manual = await f.Draft(); await Error("general_opening_required", () => f.Service.Journal.GetGeneralOpening(f.CompanyId, manual.ResourceId, f.Clerk, default));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("General opening assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
