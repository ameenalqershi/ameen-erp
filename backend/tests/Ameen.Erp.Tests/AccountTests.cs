using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class AccountTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL01: explicit contra-account nature, bilingual fields and leading zero code survive detail/search", async f =>
        {
            var r = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(),
                new("0012", "مجمع استهلاك", AccountType.Asset, f.GroupId, true, false, false, null, NormalBalance.Credit, "Accumulated depreciation", "ملاحظات الحساب"), default);
            var a = await f.Service.Account.GetAccount(f.CompanyId, r.ResourceId, f.Clerk, default);
            Ensure(a.Revision == 1 && a.Code == "0012" && a.NormalBalance == NormalBalance.Credit && a.Notes == "ملاحظات الحساب");
            var page = await f.Service.Account.ListAccounts(f.CompanyId, f.Clerk, "depreciation", 0, 10, default);
            Ensure(page.Items.Single().Id == a.Id);
            using var audit = JsonDocument.Parse(f.Store.All<AuditEvent>().Single(x => x.ResourceId == a.Id).Details);
            Ensure(audit.RootElement.GetProperty("after").GetProperty("normalBalance").GetString() == "Credit");
        });
        test("GL01: unused card can change code, classification, nature and parent with full audit", async f =>
        {
            var a = await Account(f, f.CashId); var request = Edit(a) with
            { Code = "0009", Name = "مصروف جديد", EnglishName = "New expense", Notes = "اختبار", Type = AccountType.Expense, ParentId = null, NormalBalance = NormalBalance.Credit };
            var updated = await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), request, default);
            a = await Account(f, a.Id);
            Ensure(a.Revision == 2 && a.Code == "0009" && a.Type == AccountType.Expense && a.ParentId is null && a.NormalBalance == NormalBalance.Credit && a.Version == updated.Version);
            var events = await f.Service.Account.ListAccountAudit(f.CompanyId, a.Id, f.Owner, 0, 10, default);
            using var json = JsonDocument.Parse(events.Items.Single(x => x.Action == "account.edited").Details);
            Ensure(json.RootElement.GetProperty("before").GetProperty("version").GetGuid() == request.ExpectedVersion &&
                json.RootElement.GetProperty("after").GetProperty("version").GetGuid() == updated.Version);
        });
        test("GL01: self and descendant parenting are rejected without audit or receipt", async f =>
        {
            var group = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("1010", "فرع رئيسي", AccountType.Asset, f.GroupId, false, false, false, null, NormalBalance.Debit), default);
            var root = await Account(f, f.GroupId); var count = f.Store.Count<AuditEvent>();
            foreach (var parent in new[] { root.Id, group.ResourceId })
                await Error("account_tree_cycle", () => f.Service.Account.EditAccount(f.CompanyId, root.Id, f.Owner, Fixture.Key(), Edit(root) with { ParentId = parent }, default));
            Ensure((await Account(f, root.Id)).Version == root.Version && f.Store.Count<AuditEvent>() == count);
        });
        test("GL01: posting parent, foreign classification and inactive parent are rejected", async f =>
        {
            var a = await Account(f, f.CashId); var group = await Account(f, f.GroupId);
            await Error("invalid_parent_account", () => f.Service.Account.EditAccount(f.CompanyId, group.Id, f.Owner, Fixture.Key(), Edit(group) with { ParentId = a.Id }, default));
            await Error("invalid_parent_account", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { Type = AccountType.Expense }, default));
            var p = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("1200", "معطل", AccountType.Asset, null, false, false, false, null, NormalBalance.Debit), default);
            await f.Service.Account.SetAccountStatus(f.CompanyId, p.ResourceId, f.Owner, Fixture.Key(), new(p.Version, false, "تعطيل"), default);
            await Error("invalid_parent_account", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { ParentId = p.ResourceId }, default));
        });
        test("GL01: even inactive children prevent summary-to-posting conversion or mismatched classification", async f =>
        {
            var child = await Account(f, f.CashId);
            await f.Service.Account.SetAccountStatus(f.CompanyId, child.Id, f.Owner, Fixture.Key(), new(child.Version, false, "تعطيل"), default);
            var root = await Account(f, f.GroupId);
            await Error("account_has_children", () => f.Service.Account.EditAccount(f.CompanyId, root.Id, f.Owner, Fixture.Key(), Edit(root) with { IsPosting = true }, default));
            await Error("child_account_type_mismatch", () => f.Service.Account.EditAccount(f.CompanyId, root.Id, f.Owner, Fixture.Key(), Edit(root) with { Type = AccountType.Expense }, default));
        });
        test("GL01: duplicate code and cross-company parent/currency do not mutate card", async f =>
        {
            var a = await Account(f, f.CashId);
            await Error("duplicate_account", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { Code = "3001" }, default));
            var cid = Guid.NewGuid();
            await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHER", "أخرى", "YE", "USD", 2, Fixture.Profile()), default);
            var p = await f.Service.Account.CreateAccount(cid, f.Owner, Fixture.Key(), new("P", "أب أجنبي", AccountType.Asset, null, false, false, false, null, NormalBalance.Debit), default);
            await Error("reference_not_found", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { ParentId = p.ResourceId }, default));
            await Error("currency_not_found", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { CurrencyCode = "USD" }, default));
        });
        test("GL01: draft, submitted, approved, posted, reversed and cancelled usage lock financial structure", async f =>
        {
            var draft = await f.Draft(); var a = await Account(f, f.CashId);
            var request = Edit(a) with { NormalBalance = NormalBalance.Credit };
            async Task Locked() => await Error("account_structure_in_use", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), request, default));
            await Locked(); var submitted = await f.Action(draft, "submit"); await Locked();
            var approved = await f.Action(submitted, "approve", f.Approver); await Locked();
            var posted = await f.Action(approved, "post"); await Locked();
            var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, posted.ResourceId, f.Clerk, Fixture.Key(), new(posted.Version, Fixture.Date, "عكس"), default);
            await f.Post(reverse); await Locked();
            var canceled = await f.Draft(); await f.Action(canceled, "cancel", reason: "إلغاء"); await Locked();
            var usage = await f.Service.Account.GetAccountUsage(f.CompanyId, a.Id, f.Owner, 1, 1, default);
            Ensure(usage.JournalCount == 3 && usage.JournalLineCount == 3 && usage.References.TotalCount == 3 && usage.References.Items.Count == 1);
        });
        test("GL01: every structural flag and parent/code change is locked once referenced", async f =>
        {
            await f.Draft(); var a = await Account(f, f.CashId); var r = Edit(a);
            var requests = new[] { r with { Code = "OTHER" }, r with { ParentId = null }, r with { IsPosting = false },
                r with { IsControl = true }, r with { RequiresCostCenter = true }, r with { CurrencyCode = "SAR" },
                r with { Type = AccountType.Expense, ParentId = null }, r with { NormalBalance = NormalBalance.Credit } };
            foreach (var request in requests)
                await Error("account_structure_in_use", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), request, default));
        });
        test("GL01: used descendant freezes ancestor regrouping and usage identifies the subtree", async f =>
        {
            await f.Draft(); var root = await Account(f, f.GroupId);
            await Error("account_structure_in_use", () => f.Service.Account.EditAccount(f.CompanyId, root.Id, f.Owner, Fixture.Key(), Edit(root) with { Code = "100A" }, default));
            var usage = await f.Service.Account.GetAccountUsage(f.CompanyId, root.Id, f.Owner, 0, 10, default);
            Ensure(usage.ChildAccountCount == 1 && usage.JournalCount == 0 && usage.HasUsageInSubtree && usage.References.TotalCount == 0);
        });
        test("GL01: inactive recurring template is usage even before any journal generation", async f =>
        {
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Clerk.Subject, Permissions.All, true, Array.Empty<Guid>(), member.Version), default);
            var template = await f.Service.RecurringJournal.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(),
                new(f.BranchId, "قالب", RecurringFrequency.Monthly, RecurringGenerationMode.Manual, Fixture.Date, Fixture.Date,
                    f.Request().Lines.Select(x => new RecurringLineRequest(x.AccountId, null, x.Description, x.CurrencyCode, x.Debit, x.Credit)).ToArray()), default);
            await f.Service.RecurringJournal.SetRecurringStatus(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(template.Version, false, "تعطيل"), default);
            var a = await Account(f, f.CashId);
            await Error("account_structure_in_use", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { IsControl = true }, default));
            var usage = await f.Service.Account.GetAccountUsage(f.CompanyId, a.Id, f.Owner, 0, 10, default);
            Ensure(usage.RecurringTemplateCount == 1 && usage.JournalCount == 0 && usage.References.Items.Single().State == "Inactive");
        });
        test("GL01: descriptive edits on posted account keep financial amounts and hierarchy unchanged", async f =>
        {
            await f.Post(); var a = await Account(f, f.CashId);
            await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), Edit(a) with { Name = "نقدية معدلة", EnglishName = "Cash", Notes = "وصف جديد" }, default);
            var trial = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default);
            Ensure(trial.TotalClosingDebit == 100 && trial.Rows.Single(x => x.AccountId == a.Id).Name == "نقدية معدلة");
        });
        test("GL01: replay, stale version, payload conflict and revocation are enforced for edits", async f =>
        {
            var a = await Account(f, f.CashId); var key = Fixture.Key(); var request = Edit(a) with { Name = "تعديل" };
            var r = await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default);
            Ensure(await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default) == r);
            await Error("idempotency_conflict", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request with { Name = "آخر" }, default));
            await Error("version_conflict", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), request, default));
            f.Store.All<Membership>().Single(x => x.Subject == f.Owner.Subject).SetActive(false);
            await Error("permission_denied", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default));
        });
        test("GL01: branch-limited readers cannot disclose global usage/audit or configure accounts", async f =>
        {
            var a = await Account(f, f.CashId);
            await f.Service.Account.GetAccount(f.CompanyId, a.Id, f.Limited, default);
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Limited.Subject, Permissions.All, false, new[] { f.BranchId }, member.Version), default);
            await Error("all_branches_required", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Limited, Fixture.Key(), Edit(a), default));
            await Error("all_branches_required", () => f.Service.Account.GetAccountUsage(f.CompanyId, a.Id, f.Limited, 0, 10, default));
            await Error("all_branches_required", () => f.Service.Account.ListAccountAudit(f.CompanyId, a.Id, f.Limited, 0, 10, default));
        });
        test("GL01: failed edit rolls back card, audit and idempotency receipt; exact retry succeeds", async f =>
        {
            var a = await Account(f, f.CashId); var key = Fixture.Key(); var count = f.Store.Count<AuditEvent>();
            var request = Edit(a) with { EnglishName = "Cash" }; f.Store.FailBeforeCommit = true;
            try { await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default); throw new Exception("Expected persistence failure"); }
            catch (IOException) { }
            a = await Account(f, a.Id);
            Ensure(a.EnglishName is null && a.Version == request.ExpectedVersion && f.Store.Count<AuditEvent>() == count && !f.Store.All<IdempotencyRecord>().Any(x => x.Key == key));
            await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default);
        });
        test("GL01: integer revision increments on edit/status; replay and rejection do not advance it", async f =>
        {
            var a = await Account(f, f.CashId); Ensure(a.Revision == 1); var key = Fixture.Key();
            var request = Edit(a) with { Notes = "تعديل أول" };
            var changed = await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default);
            await f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, key, request, default);
            Ensure((await Account(f, a.Id)).Revision == 2);
            var inactive = await f.Service.Account.SetAccountStatus(f.CompanyId, a.Id, f.Owner, Fixture.Key(), new(changed.Version, false, "تعطيل"), default);
            Ensure((await Account(f, a.Id)).Revision == 3);
            await Error("version_conflict", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), request, default));
            Ensure((await Account(f, a.Id)).Revision == 3);
            await f.Service.Account.SetAccountStatus(f.CompanyId, a.Id, f.Owner, Fixture.Key(), new(inactive.Version, true, "تفعيل"), default);
            Ensure((await Account(f, a.Id)).Revision == 4);
        });
        test("GL01: invalid nature, overlong labels and missing edit reason are rejected", async f =>
        {
            var a = await Account(f, f.CashId); var r = Edit(a);
            // Application serialization also rejects undefined enum values before mutation.
            await Error("invalid_command_payload", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), r with { NormalBalance = (NormalBalance)99 }, default));
            foreach (var request in new[] { r with { EnglishName = new string('A', 201) }, r with { Notes = new string('A', 2001) }, r with { Reason = " " } })
                await Error("invalid_text", () => f.Service.Account.EditAccount(f.CompanyId, a.Id, f.Owner, Fixture.Key(), request, default));
        });
    }

    public static EditAccountRequest Edit(Account a) => new(a.Version, a.Code, a.Name, a.Type, a.ParentId,
        a.IsPosting, a.IsControl, a.RequiresCostCenter, a.CurrencyCode, a.NormalBalance, a.EnglishName, a.Notes, "تحديث بطاقة الحساب");
    private static Task<Account> Account(Fixture f, Guid id) => f.Service.Account.GetAccount(f.CompanyId, id, f.Owner, default);
    private static void Ensure(bool condition) { if (!condition) throw new Exception("Account assertion failed"); }
    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
