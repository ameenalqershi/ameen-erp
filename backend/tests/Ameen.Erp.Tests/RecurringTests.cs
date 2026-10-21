using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class RecurringTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL04: generation creates a linked draft with no approval, posting or report balance", async f =>
        {
            var template = await Create(f); var result = await Generate(f, template, 10);
            var journal = await f.Journal(result.ResourceId);
            Ensure(journal.State == JournalState.Draft && journal.ApprovedBy is null && journal.PostedAt is null);
            Ensure(journal.Source == JournalSource.Recurring && journal.RecurringTemplateId == template.ResourceId && journal.SourceTemplateVersion == template.Version);
            Ensure(journal.AccountingDate == new DateOnly(2026, 10, 31) && f.Store.Count<OutboxEvent>() == 0);
            var trial = await f.Service.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default);
            Ensure(trial.TotalPeriodDebit == 0 && f.Store.Count<RecurringJournalGeneration>() == 1);
        });
        test("GL04 BR06: same key replays; another key cannot generate the same template/period", async f =>
        {
            var template = await Create(f); var key = Fixture.Key();
            var first = await Generate(f, template, 10, key);
            Ensure(await Generate(f, template, 10, key) == first);
            await Error("recurring_already_generated", () => Generate(f, template, 10));
            await f.Action(first, "cancel", reason: "إلغاء مسودة دورية");
            await Error("recurring_already_generated", () => Generate(f, template, 10));
        });
        test("GL04: future template edits preserve generated amounts and historical version", async f =>
        {
            var template = await Create(f); var first = await Generate(f, template, 10);
            var request = Request(f, 250) with { StartsOn = new(2026, 11, 15) };
            var edited = await f.Service.EditRecurringJournal(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), Edit(template.Version, request), default);
            var next = await Generate(f, edited, 11);
            Ensure((await f.Journal(first.ResourceId)).Lines.Sum(x => x.Debit) == 100);
            var nextJournal = await f.Journal(next.ResourceId);
            Ensure(nextJournal.Lines.Sum(x => x.Debit) == 250 && nextJournal.SourceTemplateVersion == edited.Version);
            Ensure((await f.Journal(first.ResourceId)).SourceTemplateVersion == template.Version);
            using (var change = JsonDocument.Parse(f.Store.All<AuditEvent>().Single(x => x.Action == "recurring.edited").Details))
            {
                Ensure(change.RootElement.GetProperty("before").GetProperty("version").GetGuid() == template.Version);
                Ensure(change.RootElement.GetProperty("after").GetProperty("version").GetGuid() == edited.Version);
            }
            var calendar = await f.Service.RecurringSchedule(f.CompanyId, template.ResourceId, f.Store.All<FiscalYear>().Single().Id, f.Clerk, default);
            Ensure(calendar.Single(x => x.FiscalPeriodId == Period(f, 10)).DueDate == new DateOnly(2026, 10, 31));
            await Error("recurring_already_generated", () => Generate(f, edited, 11));
            await Error("version_conflict", () => f.Service.EditRecurringJournal(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), Edit(template.Version, request), default));
        });
        test("GL04: inactive template blocks new generation and can be reactivated", async f =>
        {
            var template = await Create(f);
            var inactive = await f.Service.SetRecurringStatus(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(template.Version, false, "تعطيل"), default);
            await Error("recurring_inactive", () => Generate(f, inactive, 10));
            var active = await f.Service.SetRecurringStatus(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(inactive.Version, true, "تفعيل"), default);
            await Generate(f, active, 10);
        });
        test("GL04 BR18: branch scope applies to template create, detail, list, schedule and generation", async f =>
        {
            var template = await Create(f, Request(f) with { BranchId = f.OtherBranchId });
            await Grant(f, f.Limited, Permissions.All, new[] { f.BranchId });
            await Error("branch_access_denied", () => f.Service.GetRecurringJournal(f.CompanyId, template.ResourceId, f.Limited, default));
            await Error("branch_access_denied", () => f.Service.CreateRecurringJournal(f.CompanyId, f.Limited, Fixture.Key(), Request(f) with { BranchId = f.OtherBranchId }, default));
            await Error("branch_access_denied", () => f.Service.GenerateRecurringJournal(f.CompanyId, template.ResourceId, f.Limited, Fixture.Key(), new(template.Version, Period(f, 10)), default));
            await Error("branch_access_denied", () => f.Service.RecurringSchedule(f.CompanyId, template.ResourceId, f.Store.All<FiscalYear>().Single().Id, f.Limited, default));
            Ensure((await f.Service.ListRecurringJournals(f.CompanyId, f.Limited, null, null, 0, 10, default)).TotalCount == 0);
        });
        test("GL04 BR02: another company's template and period cannot be referenced", async f =>
        {
            var template = await Create(f);
            var other = Guid.NewGuid();
            await f.Service.CreateCompany(f.Owner, Fixture.Key(), new(other, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default);
            await f.Service.CreateFiscalYear(other, f.Owner, Fixture.Key(), new("2026", new(2026, 1, 1), new(2026, 12, 31)), default);
            var foreignPeriod = f.Store.All<FiscalPeriod>().First(x => x.CompanyId == other);
            await Error("reference_not_found", () => f.Service.GenerateRecurringJournal(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(template.Version, foreignPeriod.Id), default));
            await Error("permission_denied", () => f.Service.GetRecurringJournal(other, template.ResourceId, f.Clerk, default));
        });
        test("GL04 BR04 BR08: template rejects control, summary and incorrect center accounts", async f =>
        {
            await Grant(f, f.Clerk);
            var request = Request(f); request.Lines[0] = request.Lines[0] with { AccountId = f.GroupId };
            await Error("account_not_postable", () => f.Service.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), request, default));
            var control = await f.Service.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("1200", "رقابة", AccountType.Asset, f.GroupId, true, true, false, null, NormalBalance.Debit), default);
            request = Request(f); request.Lines[0] = request.Lines[0] with { AccountId = control.ResourceId };
            await Error("control_account_protected", () => f.Service.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), request, default));
            var center = await f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("OTHER", "مركز آخر", f.OtherBranchId), default);
            request = Request(f); request.Lines[0] = request.Lines[0] with { CostCenterId = center.ResourceId };
            await Error("cost_center_mismatch", () => f.Service.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), request, default));
        });
        test("GL04: generation revalidates account status and leaves no sequence or occurrence on failure", async f =>
        {
            var template = await Create(f); var account = f.Store.All<Account>().Single(x => x.Id == f.CashId);
            await f.Service.SetAccountStatus(f.CompanyId, account.Id, f.Owner, Fixture.Key(), new(account.Version, false, "تعطيل"), default);
            await Error("account_not_postable", () => Generate(f, template, 10));
            Ensure(f.Store.Count<JournalEntry>() == 0 && f.Store.Count<NumberSequence>() == 0 && f.Store.Count<RecurringJournalGeneration>() == 0);
        });
        test("GL04 BR10: foreign rate is selected at each due date and original snapshot stays fixed", async f =>
        {
            await f.Service.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار أمريكي"), default);
            var request = Request(f); request.Lines[0] = request.Lines[0] with { CurrencyCode = "USD" };
            request.Lines[1] = request.Lines[1] with { Credit = 375 };
            var template = await Create(f, request);
            await Error("exchange_rate_required", () => Generate(f, template, 10));
            await f.Service.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", new(2026, 10, 1), 3.75m, "أكتوبر"), default);
            await f.Service.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", new(2026, 11, 1), 4m, "نوفمبر"), default);
            var october = await Generate(f, template, 10); var november = await Generate(f, template, 11);
            Ensure((await f.Journal(october.ResourceId)).Lines[0].ExchangeRate == 3.75m);
            Ensure((await f.Journal(november.ResourceId)).Lines[0].FunctionalDebit == 400);
            await Error("journal_unbalanced", () => f.Action(november, "submit"));
        });
        test("GL04: monthly end-of-month anchor does not drift across leap February", async f =>
        {
            await f.Service.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), new("2028", new(2028, 1, 1), new(2028, 12, 31)), default);
            var template = await Create(f, Request(f) with { StartsOn = new(2028, 1, 31), EndsOn = new(2028, 3, 31) });
            var dates = new[] { new DateOnly(2028, 1, 31), new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 31) };
            foreach (var date in dates)
            {
                var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Year == 2028 && x.StartsOn.Month == date.Month);
                var r = await f.Service.GenerateRecurringJournal(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(template.Version, period.Id), default);
                Ensure((await f.Journal(r.ResourceId)).AccountingDate == date);
            }
        });
        test("GL04: quarterly/yearly cadence skips periods without an occurrence", async f =>
        {
            var quarterly = await Create(f, Request(f) with { Frequency = RecurringFrequency.Quarterly });
            await Error("recurring_not_due", () => Generate(f, quarterly, 11));
            var calendar = await f.Service.RecurringSchedule(f.CompanyId, quarterly.ResourceId, f.Store.All<FiscalYear>().Single().Id, f.Clerk, default);
            Ensure(calendar.Select(x => x.DueDate).SequenceEqual(new[] { new DateOnly(2026, 1, 31), new(2026, 4, 30), new(2026, 7, 31), new(2026, 10, 31) }));
            var yearly = await Create(f, Request(f) with { Name = "سنوي", Frequency = RecurringFrequency.Yearly });
            await Error("recurring_not_due", () => Generate(f, yearly, 10));
            Ensure((await f.Service.RecurringSchedule(f.CompanyId, yearly.ResourceId, f.Store.All<FiscalYear>().Single().Id, f.Clerk, default)).Count == 1);
        });
        test("GL04 BR03: closed period rejects generation", async f =>
        {
            var template = await Create(f); var period = f.Store.All<FiscalPeriod>().Single(x => x.Id == Period(f, 10));
            await f.Service.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default);
            await Error("period_closed", () => Generate(f, template, 10));
            Ensure(f.Store.Count<RecurringJournalGeneration>() == 0);
        });
        test("GL04 BR05: failed generation rolls back draft, sequence, audit and receipt", async f =>
        {
            var template = await Create(f); var count = f.Store.Count<AuditEvent>(); var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await Generate(f, template, 10, key); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure(f.Store.Count<RecurringJournalGeneration>() == 0 && f.Store.Count<JournalEntry>() == 0 && f.Store.Count<NumberSequence>() == 0 && f.Store.Count<AuditEvent>() == count);
            Ensure((await Generate(f, template, 10, key)).Number == "JV-2026-000001");
        });
        test("GL04: generated draft uses independent approval, posting, reporting and linked reversal", async f =>
        {
            var template = await Create(f); var generated = await Generate(f, template, 10);
            var submitted = await f.Action(generated, "submit");
            await Error("self_approval_forbidden", () => f.Action(submitted, "approve"));
            var approved = await f.Action(submitted, "approve", f.Approver); var posted = await f.Action(approved, "post");
            var trial = await f.Service.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default);
            Ensure(trial.TotalClosingDebit == 100);
            var reversal = await f.Service.ReverseJournal(f.CompanyId, posted.ResourceId, f.Clerk, Fixture.Key(), new(posted.Version, new(2026, 11, 1), "تصحيح"), default);
            await f.Post(reversal);
            Ensure((await f.Journal(reversal.ResourceId)).Source == JournalSource.Reversal);
            Ensure((await f.Service.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default, JournalSource.Recurring)).TotalCount == 1);
            Ensure((await f.Service.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default, JournalSource.Manual)).TotalCount == 0);
            Ensure((await f.Service.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default)).TotalClosingDebit == 0);
        });
        test("GL04: configuration admin has no implicit template management permission", async f =>
        {
            await Error("permission_denied", () => f.Service.CreateRecurringJournal(f.CompanyId, f.Owner, Fixture.Key(), Request(f), default));
            var template = await Create(f);
            await Grant(f, f.Clerk, new[] { Permissions.Read, Permissions.RecurringGenerate });
            await Error("permission_denied", () => Generate(f, template, 10));
        });
        test("GL04: permissions are rechecked before generation replay", async f =>
        {
            var template = await Create(f); var key = Fixture.Key(); await Generate(f, template, 10, key);
            await Grant(f, f.Clerk, new[] { Permissions.Read, Permissions.Draft });
            await Error("permission_denied", () => Generate(f, template, 10, key));
        });
        test("GL04: usage history preserves cancellation and supports pagination", async f =>
        {
            var template = await Create(f); var october = await Generate(f, template, 10); var november = await Generate(f, template, 11);
            await f.Action(november, "cancel", reason: "إلغاء");
            var history = await f.Service.ListRecurringGenerations(f.CompanyId, template.ResourceId, f.Clerk, 0, 1, default);
            Ensure(history.TotalCount == 2 && history.Items.Single().JournalId == november.ResourceId);
            var calendar = await f.Service.RecurringSchedule(f.CompanyId, template.ResourceId, f.Store.All<FiscalYear>().Single().Id, f.Clerk, default);
            Ensure(calendar.Single(x => x.GeneratedJournalId == october.ResourceId).GeneratedState == JournalState.Draft);
            Ensure(calendar.Single(x => x.GeneratedJournalId == november.ResourceId).GeneratedState == JournalState.Cancelled);
        });
        test("GL04: invalid cadence, mode and dates fail; finite window is enforced", async f =>
        {
            await Grant(f, f.Clerk);
            foreach (var (request, code) in new[]
            {
                (Request(f) with { Frequency = (RecurringFrequency)99 }, "invalid_command_payload"),
                (Request(f) with { GenerationMode = (RecurringGenerationMode)1 }, "invalid_command_payload"),
                (Request(f) with { EndsOn = new(2025, 1, 1) }, "invalid_recurring_dates")
            }) await Error(code, () => f.Service.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), request, default));
            var template = await Create(f, Request(f) with { EndsOn = new(2026, 2, 15) });
            await Error("recurring_not_due", () => Generate(f, template, 2));
            var boundary = new RecurringJournalTemplate(f.CompanyId, f.BranchId, "حد التاريخ", RecurringFrequency.Monthly, RecurringGenerationMode.Manual,
                new(9999, 12, 31), new(9999, 12, 31), f.Clerk.Subject, DateTimeOffset.UtcNow);
            Ensure(boundary.DueDateFor(new(9999, 12, 1), new(9999, 12, 31)) == new DateOnly(9999, 12, 31));
        });
        test("GL04: persisted journal provenance and generation history are immutable", async f =>
        {
            var template = await Create(f); var generated = await Generate(f, template, 10);
            using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true").Options);
            var journal = await f.Journal(generated.ResourceId); db.Attach(journal);
            db.Entry(journal).Property(x => x.Source).CurrentValue = JournalSource.Manual;
            await Error("journal_source_immutable", () => db.SaveChangesAsync());
            db.ChangeTracker.Clear(); db.Remove(f.Store.All<RecurringJournalGeneration>().Single());
            await Error("recurring_generation_immutable", () => db.SaveChangesAsync());
        });
        test("GL04: search and card change log are scoped and require audit permission", async f =>
        {
            var template = await Create(f);
            Ensure((await f.Service.ListRecurringJournals(f.CompanyId, f.Clerk, null, true, 0, 10, default, "دوري")).TotalCount == 1);
            Ensure((await f.Service.ListRecurringJournals(f.CompanyId, f.Clerk, null, true, 0, 10, default, "غير موجود")).TotalCount == 0);
            var changes = await f.Service.ListRecurringChanges(f.CompanyId, template.ResourceId, f.Clerk, 0, 10, default);
            Ensure(changes.TotalCount == 1 && changes.Items.Single().Action == "recurring.created");
            using (var change = JsonDocument.Parse(changes.Items.Single().Details))
                Ensure(change.RootElement.GetProperty("version").GetGuid() == template.Version);
            await Grant(f, f.Limited, new[] { Permissions.Read }, new[] { f.BranchId });
            await Error("permission_denied", () => f.Service.ListRecurringChanges(f.CompanyId, template.ResourceId, f.Limited, 0, 10, default));
        });
    }

    private static CreateRecurringJournalRequest Request(Fixture f, decimal amount = 100) => new(f.BranchId, "قالب دوري", RecurringFrequency.Monthly,
        RecurringGenerationMode.Manual, new(2026, 1, 31), new(2026, 12, 31),
        new[] { new RecurringLineRequest(f.CashId, null, "مدين", "SAR", amount, 0), new RecurringLineRequest(f.EquityId, null, "دائن", "SAR", 0, amount) });
    private static EditRecurringJournalRequest Edit(Guid version, CreateRecurringJournalRequest r) => new(version, r.Name, r.Frequency, r.GenerationMode, r.StartsOn, r.EndsOn, r.Lines);
    private static async Task Grant(Fixture f, Actor actor, string[]? permissions = null, Guid[]? branches = null)
    {
        var member = f.Store.All<Membership>().Single(x => x.Subject == actor.Subject);
        await f.Service.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(actor.Subject, permissions ?? Permissions.All, branches is null, branches ?? Array.Empty<Guid>(), member.Version), default);
    }
    private static async Task<CommandReceipt> Create(Fixture f, CreateRecurringJournalRequest? request = null)
    {
        await Grant(f, f.Clerk);
        return await f.Service.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(), request ?? Request(f), default);
    }
    private static Guid Period(Fixture f, int month) => f.Store.All<FiscalPeriod>().Single(x => x.CompanyId == f.CompanyId && x.StartsOn.Year == 2026 && x.StartsOn.Month == month).Id;
    private static Task<CommandReceipt> Generate(Fixture f, CommandReceipt template, int month, string? key = null) =>
        f.Service.GenerateRecurringJournal(f.CompanyId, template.ResourceId, f.Clerk, key ?? Fixture.Key(), new(template.Version, Period(f, month)), default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Recurring journal assertion failed"); }
    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected business error " + code);
    }
}
