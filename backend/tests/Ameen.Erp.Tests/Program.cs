using System.Security.Claims;
using System.Text.Json;
using Ameen.Erp.Api;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Ameen.Erp.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Fixture, Task> action) => tests.Add((name, async () => { var f = new Fixture(); await f.Initialize(); await action(f); }));
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task Error(string code, Func<Task> action)
{
    try { await action(); }
    catch (BusinessException ex) { Assert(ex.Code == code, $"Expected {code}, got {ex.Code}"); return; }
    throw new Exception("Expected business error: " + code);
}

Test("BR01 AT01: unbalanced journal cannot submit and remains draft", async f =>
{
    var draft = await f.Draft(f.Request(100, 99));
    await Error("journal_unbalanced", () => f.Action(draft, "submit"));
    Assert((await f.Journal(draft.ResourceId)).State == JournalState.Draft);
    Assert(f.Store.Count<OutboxEvent>() == 0);
});
Test("GL03-T01/T02: balanced posting reaches ledger while rejected journal leaves no financial effect", async f =>
{
    var accepted = await f.Draft(f.Request(100, 100));
    var rejected = await f.Draft(f.Request(100, 99));
    var beforeOutbox = f.Store.Count<OutboxEvent>();
    await Error("journal_unbalanced", () => f.Action(rejected, "submit"));
    Assert((await f.Journal(rejected.ResourceId)).State == JournalState.Draft);
    var submitted = await f.Action(accepted, "submit");
    var approved = await f.Action(submitted, "approve", f.Approver);
    await f.Action(approved, "post", f.Approver);
    Assert((await f.Journal(accepted.ResourceId)).State == JournalState.Posted);
    Assert((await f.Journal(rejected.ResourceId)).State == JournalState.Draft);
    Assert(f.Store.Count<OutboxEvent>() == beforeOutbox + 1);
    var ledger = await f.Service.Report.AccountLedger(f.CompanyId, f.CashId, f.Clerk,
        new(2026, 10, 1), new(2026, 10, 31), null, 0, 10, default);
    Assert(ledger.Entries.TotalCount == 1 && ledger.ClosingBalance == 100,
        "Rejected journal affected the ledger or valid posting was missing");
});
Test("BR01: line must contain exactly one positive side", async f =>
{
    var request = f.Request(); request.Lines[0] = request.Lines[0] with { Credit = 1 };
    await Error("invalid_journal_side", () => f.Draft(request));
    Assert(f.Store.Count<JournalEntry>() == 0);
});
Test("GL03-T02: zero-side journal line rejects without persisting journal, audit or outbox", async f =>
{
    var initialJournals = f.Store.Count<JournalEntry>();
    var initialAudits = f.Store.Count<AuditEvent>();
    var initialOutbox = f.Store.Count<OutboxEvent>();
    var initialReceipts = f.Store.Count<IdempotencyRecord>();
    var request = f.Request();
    request.Lines[0] = request.Lines[0] with { Debit = 0, Credit = 0 };
    await Error("invalid_journal_side", () => f.Draft(request));
    Assert(f.Store.Count<JournalEntry>() == initialJournals &&
        f.Store.Count<AuditEvent>() == initialAudits &&
        f.Store.Count<OutboxEvent>() == initialOutbox &&
        f.Store.Count<IdempotencyRecord>() == initialReceipts,
        "Rejected zero-side line changed journal, audit, outbox or receipt state");
});
Test("BR01: disallow negative or excessive precision amounts", async f =>
{
    await Error("invalid_amount", () => f.Draft(f.Request(-1, 1)));
    await Error("invalid_amount", () => f.Draft(f.Request(1.001m, 1.001m)));
});
Test("BR03 AT02: closed period rejects journal", async f =>
{
    var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Month == 10);
    await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال اختباري"), true, default);
    await Error("period_closed", () => f.Draft());
});
Test("GL03 AT02: an open document date cannot bypass a closed accounting period", async f =>
{
    var closed = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn == new DateOnly(2026, 10, 1));
    await f.Service.PeriodClose.ChangePeriod(f.CompanyId, closed.Id, f.Approver, Fixture.Key(),
        new(closed.Version, "إقفال اختباري"), true, default);
    var november = new DateOnly(2026, 11, 2);
    var journals = f.Store.Count<JournalEntry>();
    var audits = f.Store.Count<AuditEvent>();
    var receipts = f.Store.Count<IdempotencyRecord>();
    try
    {
        await f.Draft(f.Request() with { DocumentDate = november });
        throw new Exception("Document date bypassed a closed accounting period");
    }
    catch (BusinessException ex) when (ex.Code == "period_closed")
    {
        Assert(ex.Message.Contains(closed.Code, StringComparison.Ordinal),
            "Closed-period error must identify the accounting period");
    }
    Assert(f.Store.Count<JournalEntry>() == journals && f.Store.Count<AuditEvent>() == audits &&
        f.Store.Count<IdempotencyRecord>() == receipts && f.Store.Count<OutboxEvent>() == 0,
        "Rejected closed-period draft had durable side effects");

    // The converse is valid: the document date alone does not determine posting period.
    var valid = await f.Draft(f.Request(date: november) with { DocumentDate = Fixture.Date });
    var original = await f.Journal(valid.ResourceId);
    Assert(original.DocumentDate == Fixture.Date && original.AccountingDate == november);
    audits = f.Store.Count<AuditEvent>();
    receipts = f.Store.Count<IdempotencyRecord>();
    var invalidHeader = new DraftJournalHeaderRequest(f.BranchId, november, Fixture.Date,
        "AT02-BYPASS", null);
    try
    {
        await f.Service.Journal.EditJournal(f.CompanyId, valid.ResourceId, f.Clerk, Fixture.Key(),
            new(valid.Version, "محاولة تغيير تاريخ الوثيقة", f.Request().Lines, invalidHeader), default);
        throw new Exception("Draft header edit bypassed a closed accounting period");
    }
    catch (BusinessException ex) when (ex.Code == "period_closed")
    {
        Assert(ex.Message.Contains(closed.Code, StringComparison.Ordinal),
            "Edited journal must identify the closed accounting period");
    }
    var after = await f.Journal(valid.ResourceId);
    Assert(after.Version == original.Version && after.DocumentDate == original.DocumentDate &&
        after.AccountingDate == original.AccountingDate && after.FiscalPeriodId == original.FiscalPeriodId &&
        after.Number == original.Number && f.Store.Count<AuditEvent>() == audits &&
        f.Store.Count<IdempotencyRecord>() == receipts && f.Store.Count<OutboxEvent>() == 0,
        "Rejected closed-period header edit modified the draft or its history");
    await f.Post(valid);
    Assert((await f.Journal(valid.ResourceId)).State == JournalState.Posted,
        "Valid accounting date in an open period must remain postable");
});
Test("BR03: period closure rejects outstanding drafts", async f =>
{
    await f.Draft(); var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Month == 10);
    await Error("unposted_journals", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default));
    Assert(!f.Store.All<FiscalPeriod>().Single(x => x.Id == period.Id).IsClosed);
});
Test("BR03: reopening requires separate privilege and reason", async f =>
{
    var p = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Month == 10);
    var closed = await f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(p.Version, "إقفال"), true, default);
    await Error("permission_denied", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Clerk, Fixture.Key(), new(closed.Version, "فتح"), false, default));
    await f.Service.PeriodClose.ChangePeriod(f.CompanyId, p.Id, f.Approver, Fixture.Key(), new(closed.Version, "تصحيح معتمد"), false, default);
    await f.Draft();
});
Test("BR09: journal creator cannot approve own journal", async f =>
{
    var r = await f.Action(await f.Draft(), "submit");
    await Error("self_approval_forbidden", () => f.Action(r, "approve"));
    Assert((await f.Journal(r.ResourceId)).State == JournalState.Submitted);
});
Test("Workflow: post requires approved status", async f =>
{
    await Error("journal_state_conflict", async () => { await f.Action(await f.Draft(), "post"); });
});
Test("BR05: persistence failure rolls back state, audit, sequence and receipt", async f =>
{
    var audits = f.Store.Count<AuditEvent>(); var receipts = f.Store.Count<IdempotencyRecord>();
    f.Store.FailBeforeCommit = true; var key = Fixture.Key();
    try { await f.Draft(key: key); throw new Exception("Expected failure"); } catch (IOException) { }
    Assert(f.Store.Count<JournalEntry>() == 0 && f.Store.Count<NumberSequence>() == 0);
    Assert(f.Store.Count<AuditEvent>() == audits && f.Store.Count<IdempotencyRecord>() == receipts);
    Assert((await f.Draft(key: key)).Number == "JV-2026-000001");
});
Test("BR05: failed posting leaves approved journal and no outbox", async f =>
{
    var r = await f.Action(await f.Draft(), "submit"); r = await f.Action(r, "approve", f.Approver);
    var audits = f.Store.Count<AuditEvent>(); f.Store.FailBeforeCommit = true;
    try { await f.Action(r, "post"); throw new Exception("Expected failure"); } catch (IOException) { }
    Assert((await f.Journal(r.ResourceId)).State == JournalState.Approved && f.Store.Count<OutboxEvent>() == 0);
    Assert(f.Store.Count<AuditEvent>() == audits);
});
Test("BR06 AT03: replay posting returns identical receipt and one outbox", async f =>
{
    var r = await f.Action(await f.Draft(), "submit"); r = await f.Action(r, "approve", f.Approver);
    var key = Fixture.Key(); var first = await f.Action(r, "post", key: key);
    var again = await f.Action(r, "post", key: key);
    Assert(first == again && f.Store.Count<OutboxEvent>() == 1);
});
Test("BR06: simultaneous identical create requests have one result", async f =>
{
    var key = Fixture.Key(); var request = f.Request();
    var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => f.Draft(request, key)));
    Assert(results.Select(x => x.ResourceId).Distinct().Count() == 1 && f.Store.Count<JournalEntry>() == 1);
});
Test("BR06: reusing key for different payload fails", async f =>
{
    var key = Fixture.Key(); await f.Draft(key: key);
    await Error("idempotency_conflict", () => f.Draft(f.Request(200, 200), key));
});
Test("BR06: revoked permission is checked before idempotent replay", async f =>
{
    var key = Fixture.Key(); await f.Draft(key: key);
    var membership = f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject);
    await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
        new(f.Clerk.Subject, new[] { Permissions.Read }, true, Array.Empty<Guid>(), membership.Version), default);
    await Error("permission_denied", () => f.Draft(key: key));
});
Test("BR07: stale update cannot overwrite a draft", async f =>
{
    var r = await f.Draft();
    await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "تعديل", f.Request(200, 200).Lines), default);
    await Error("version_conflict", () => f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "تعديل قديم", f.Request().Lines), default));
});
Test("BR07: posted document rejects editing and cancellation", async f =>
{
    var r = await f.Post();
    await Error("journal_state_conflict", () => f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "تعديل", f.Request().Lines), default));
    await Error("journal_state_conflict", () => f.Action(r, "cancel", reason: "إلغاء"));
});
Test("BR07: reversal remains draft until separately approved and posted", async f =>
{
    var original = await f.Post();
    var reversal = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date.AddDays(1), "تصحيح"), default);
    Assert((await f.Journal(original.ResourceId)).State == JournalState.Posted);
    Assert((await f.Journal(reversal.ResourceId)).State == JournalState.Draft);
    await f.Post(reversal);
    Assert((await f.Journal(original.ResourceId)).State == JournalState.Reversed);
    var balance = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default);
    Assert(balance.TotalClosingDebit == 0 && balance.TotalClosingCredit == 0 && balance.TotalPeriodDebit == 200);
});
Test("BR07: reserve one reversal and release it on cancellation", async f =>
{
    var original = await f.Post();
    var reversal = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "تصحيح"), default);
    var fresh = await f.Journal(original.ResourceId);
    await Error("already_reversed", () => f.Service.Journal.ReverseJournal(f.CompanyId, fresh.Id, f.Clerk, Fixture.Key(), new(fresh.Version, Fixture.Date, "ثان"), default));
    await f.Action(reversal, "cancel", reason: "سحب طلب العكس");
    fresh = await f.Journal(original.ResourceId);
    await f.Service.Journal.ReverseJournal(f.CompanyId, fresh.Id, f.Clerk, Fixture.Key(), new(fresh.Version, Fixture.Date, "تصحيح آخر"), default);
});
Test("BR07: reversal draft cannot replace source amounts", async f =>
{
    var original = await f.Post();
    var reversal = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "تصحيح"), default);
    await Error("reversal_immutable", () => f.Service.Journal.EditJournal(f.CompanyId, reversal.ResourceId, f.Clerk, Fixture.Key(), new(reversal.Version, "تغيير", f.Request(50, 50).Lines), default));
});
Test("BR08: control accounts reject manual journals", async f =>
{
    var control = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("1200", "رقابة العملاء", AccountType.Asset, f.GroupId, true, true, false, null, NormalBalance.Debit), default);
    var request = f.Request(); request.Lines[0] = request.Lines[0] with { AccountId = control.ResourceId };
    await Error("control_account_protected", () => f.Draft(request));
});
Test("BR04: parent and inactive accounts reject posting", async f =>
{
    var request = f.Request(); request.Lines[0] = request.Lines[0] with { AccountId = f.GroupId };
    await Error("account_not_postable", () => f.Draft(request));
    var r = await f.Action(await f.Draft(), "submit"); r = await f.Action(r, "approve", f.Approver);
    var account = f.Store.All<Account>().Single(x => x.Id == f.CashId);
    await f.Service.Account.SetAccountStatus(f.CompanyId, account.Id, f.Owner, Fixture.Key(), new(account.Version, false, "تعطيل"), default);
    await Error("account_not_postable", () => f.Action(r, "post"));
});
Test("BR04: required cost center and center branch are validated", async f =>
{
    var expense = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("5001", "مصروف", AccountType.Expense, null, true, false, true, null, NormalBalance.Debit), default);
    var request = f.Request(); request.Lines[0] = request.Lines[0] with { AccountId = expense.ResourceId };
    await Error("cost_center_required", () => f.Draft(request));
    var center = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("C", "مركز", f.OtherBranchId), default);
    request.Lines[0] = request.Lines[0] with { CostCenterId = center.ResourceId };
    await Error("cost_center_mismatch", () => f.Draft(request));
});
Test("BR02 AT25: another company account cannot be referenced", async f =>
{
    var other = Guid.NewGuid();
    await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(other, "OTHER", "أخرى", "SA", "SAR", 2, Fixture.Profile()), default);
    var account = await f.Service.Account.CreateAccount(other, f.Owner, Fixture.Key(), new("1", "حساب", AccountType.Asset, null, true, false, false, null, NormalBalance.Debit), default);
    var request = f.Request(); request.Lines[0] = request.Lines[0] with { AccountId = account.ResourceId };
    await Error("reference_not_found", () => f.Draft(request));
    await Error("permission_denied", () => f.Service.Company.GetCompany(other, f.Clerk, default));
});
Test("BR18: branch scope applies to create, detail and reports", async f =>
{
    var posted = await f.Post(await f.Draft(f.Request(branch: f.OtherBranchId)));
    await Error("branch_access_denied", () => f.Draft(f.Request(branch: f.OtherBranchId), actor: f.Limited));
    await Error("branch_access_denied", () => f.Service.Journal.GetJournal(f.CompanyId, posted.ResourceId, f.Limited, default));
    var report = await f.Service.Report.TrialBalance(f.CompanyId, f.Limited, new(2026, 1, 1), new(2026, 12, 31), null, default);
    Assert(report.TotalPeriodDebit == 0 && report.BranchIds.SequenceEqual(new[] { f.BranchId }));
});
Test("BR09: configuration administrator has no implicit financial rights", async f =>
{
    await Error("permission_denied", () => f.Draft(actor: f.Owner));
    var member = f.Store.All<Membership>().Single(x => x.Subject == f.Owner.Subject);
    await Error("self_grant_forbidden", () => f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Owner.Subject, Permissions.All, true, Array.Empty<Guid>(), member.Version), default));
});
Test("BR10: missing, negative and future exchange rates fail", async f =>
{
    await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار أمريكي"), default);
    await Error("invalid_exchange_rate", () => f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", Fixture.Date, -1, "اختبار"), default));
    var req = f.Request(); req.Lines[0] = req.Lines[0] with { CurrencyCode = "USD" };
    await Error("exchange_rate_required", () => f.Draft(req));
    var rate = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", Fixture.Date.AddDays(1), 3.75m, "اختبار"), default);
    req.Lines[0] = req.Lines[0] with { ExchangeRateId = rate.ResourceId };
    await Error("exchange_rate_mismatch", () => f.Draft(req));
});
Test("BR10: mixed currencies balance functionally and preserve historical reversal rate", async f =>
{
    await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار أمريكي"), default);
    var rate = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", Fixture.Date, 3.75m, "اختبار"), default);
    var req = f.Request(100, 375); req.Lines[0] = req.Lines[0] with { CurrencyCode = "USD", ExchangeRateId = rate.ResourceId };
    var original = await f.Post(await f.Draft(req));
    await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", Fixture.Date.AddDays(1), 4, "سعر جديد"), default);
    var reversal = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date.AddDays(2), "تصحيح"), default);
    var line = (await f.Journal(reversal.ResourceId)).Lines.Single(x => x.CurrencyCode == "USD");
    Assert(line.ExchangeRate == 3.75m && line.FunctionalCredit == 375 && line.Credit == 100);
});
Test("RPT01 RPT02: opening, movement, closing and running page balances reconcile", async f =>
{
    await f.Post(await f.Draft(f.Request(100, 100, date: new(2026, 1, 1))));
    await f.Post(await f.Draft(f.Request(50, 50, date: new(2026, 10, 1))));
    await f.Post(await f.Draft(f.Request(25, 25, date: new(2026, 10, 2))));
    await f.Draft(f.Request(999, 999));
    var report = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 10, 1), new(2026, 10, 31), null, default);
    Assert(report.TotalOpeningDebit == 100 && report.TotalPeriodDebit == 75 && report.TotalClosingCredit == 175);
    var ledger = await f.Service.Report.AccountLedger(f.CompanyId, f.CashId, f.Clerk, new(2026, 10, 1), new(2026, 10, 31), null, 1, 1, default);
    Assert(ledger.OpeningBalance == 100 && ledger.ClosingBalance == 175 && ledger.Entries.Items.Single().RunningBalance == 175 && ledger.Entries.TotalCount == 2);
});
Test("RPT01: historical report excludes later reversal", async f =>
{
    var original = await f.Post();
    var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date.AddDays(1), "تصحيح"), default);
    await f.Post(reverse);
    var historical = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), Fixture.Date, null, default);
    Assert(historical.TotalClosingDebit == 100 && historical.TotalClosingCredit == 100);
});
Test("RPT01: account level replaces descendants without double counting", async f =>
{
    await f.Post();
    var group = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("1200", "أصول أخرى", AccountType.Asset, f.GroupId, false, false, false, null, NormalBalance.Debit), default);
    var leaf = await f.Service.Account.CreateAccount(f.CompanyId, f.Owner, Fixture.Key(), new("1201", "حساب أصول", AccountType.Asset, group.ResourceId, true, false, false, null, NormalBalance.Debit), default);
    var request = f.Request(25, 25); request.Lines[0] = request.Lines[0] with { AccountId = leaf.ResourceId };
    await f.Post(await f.Draft(request));
    var root = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default, accountLevel: 1);
    Assert(root.Rows.Count == 2 && root.TotalPeriodDebit == 125 && root.TotalClosingCredit == 125);
    Assert(root.Rows.Single(x => x.AccountId == f.GroupId).ClosingDebit == 125);
    var second = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default, accountLevel: 2);
    Assert(second.Rows.Count == 3 && second.TotalPeriodDebit == 125);
    Assert(second.Rows.Single(x => x.AccountId == group.ResourceId) is { Level: 2, IsPosting: false, ClosingDebit: 25 });
    var leaves = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default);
    Assert(leaves.Rows.All(x => x.IsPosting) && leaves.Rows.Any(x => x.AccountId == leaf.ResourceId && x.Level == 3));
});
Test("RPT01: hide zero retains accounts with offsetting posted movement", async f =>
{
    var empty = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default, showZero: false);
    Assert(empty.Rows.Count == 0);
    var original = await f.Post();
    var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "عكس"), default);
    await f.Post(reverse);
    var result = await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default, showZero: false);
    Assert(result.Rows.Count == 2 && result.Rows.All(x => x.ClosingDebit == 0 && x.ClosingCredit == 0));
    Assert(result.TotalPeriodDebit == 200 && result.TotalPeriodCredit == 200);
});
Test("RPT01 RPT02: cost-center filter applies to opening, movement and running balance", async f =>
{
    var center = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("C1", "مركز أول", f.BranchId), default);
    var other = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("C2", "مركز آخر", f.BranchId), default);
    async Task Entry(decimal amount, Guid centerId, DateOnly date)
    {
        var r = f.Request(amount, amount, date: date);
        r = r with { Lines = r.Lines.Select(x => x with { CostCenterId = centerId }).ToArray() };
        await f.Post(await f.Draft(r));
    }
    await Entry(100, center.ResourceId, new(2026, 1, 1));
    await Entry(50, center.ResourceId, Fixture.Date);
    await Entry(999, other.ResourceId, Fixture.Date);
    var trial = await f.Service.Report.TrialBalance(f.CompanyId, f.Limited, new(2026, 10, 1), new(2026, 10, 31), null, default, center.ResourceId);
    Assert(trial.TotalOpeningDebit == 100 && trial.TotalPeriodDebit == 50 && trial.TotalClosingCredit == 150);
    var ledger = await f.Service.Report.AccountLedger(f.CompanyId, f.CashId, f.Limited, new(2026, 10, 1), new(2026, 10, 31), null, 0, 10, default, center.ResourceId);
    Assert(ledger.OpeningBalance == 100 && ledger.ClosingBalance == 150 && ledger.Entries.Items.Single().RunningBalance == 150);
    Assert(ledger.CompanyId == f.CompanyId && ledger.BranchIds!.SequenceEqual(new[] { f.BranchId }));
});
Test("RPT01 RPT02: cost-center scope cannot bypass company or branch permissions", async f =>
{
    var other = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("OTHER", "غير مصرح", f.OtherBranchId), default);
    await Error("branch_access_denied", () => f.Service.Report.TrialBalance(f.CompanyId, f.Limited, Fixture.Date, Fixture.Date, null, default, other.ResourceId));
    await Error("branch_access_denied", () => f.Service.Report.AccountLedger(f.CompanyId, f.CashId, f.Limited, Fixture.Date, Fixture.Date, null, 0, 10, default, other.ResourceId));
    await Error("cost_center_mismatch", () => f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, f.BranchId, default, other.ResourceId));
    await Error("reference_not_found", () => f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default, Guid.NewGuid()));
});
Test("RPT02: external reference filters opening and movement with exact matching", async f =>
{
    await f.Post(await f.Draft(f.Request(100, 100, date: new(2026, 1, 1)) with { ExternalReference = "REF-A" }));
    await f.Post(await f.Draft(f.Request(50, 50) with { ExternalReference = "REF-A" }));
    await f.Post(await f.Draft(f.Request(999, 999) with { ExternalReference = "REF-B" }));
    var ledger = await f.Service.Report.AccountLedger(f.CompanyId, f.CashId, f.Clerk, new(2026, 10, 1), new(2026, 10, 31), null, 0, 10, default, externalReference: "REF-A");
    Assert(ledger.OpeningBalance == 100 && ledger.ClosingBalance == 150 && ledger.Entries.TotalCount == 1);
    var missing = await f.Service.Report.AccountLedger(f.CompanyId, f.CashId, f.Clerk, new(2026, 10, 1), new(2026, 10, 31), null, 0, 10, default, externalReference: "ref-a");
    Assert(missing.OpeningBalance == 0 && missing.Entries.TotalCount == 0);
});
Test("RPT01: invalid hierarchy level fails validation", async f =>
{
    foreach (var level in new[] { 0, 21 })
        await Error("invalid_account_level", () => f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default, accountLevel: level));
});
Test("SYS03: fiscal calendar has no gaps including leap February", async f =>
{
    await f.Service.FiscalCalendar.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), new("2028", new(2028, 1, 15), new(2028, 12, 31)), default);
    var periods = f.Store.All<FiscalPeriod>().Where(x => x.StartsOn.Year == 2028).OrderBy(x => x.StartsOn).ToArray();
    Assert(periods.Length == 12 && periods[1].EndsOn == new DateOnly(2028, 2, 29));
    for (var i = 1; i < periods.Length; i++) Assert(periods[i - 1].EndsOn.AddDays(1) == periods[i].StartsOn);
    await Error("fiscal_year_overlap", () => f.Service.FiscalCalendar.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), new("BAD", new(2028, 12, 1), new(2029, 1, 1)), default));
});
Test("SYS10: cancelled document numbers are never reused", async f =>
{
    var r = await f.Draft(); await f.Action(r, "cancel", reason: "إلغاء");
    Assert((await f.Draft()).Number == "JV-2026-000002");
});
Test("SYS06: disabled membership immediately loses read and replay access", async f =>
{
    var key = Fixture.Key(); await f.Draft(key: key);
    var member = f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject);
    await f.Service.Membership.SetMembershipStatus(f.CompanyId, member.Id, f.Owner, Fixture.Key(), new(member.Version, false, "تعطيل"), default);
    await Error("permission_denied", () => f.Draft(key: key));
    await Error("permission_denied", () => f.Service.Account.ListAccounts(f.CompanyId, f.Clerk, null, 0, 10, default));
});
Test("BR07: EF persistence rejects posted line changes through both save paths", async f =>
{
    var posted = await f.Post();
    using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
        .UseSqlServer("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true").Options);
    var journal = await f.Journal(posted.ResourceId); db.Attach(journal);
    db.Entry(journal.Lines[0]).Property(x => x.Debit).CurrentValue = 999;
    await Error("journal_lines_immutable", () => db.SaveChangesAsync());
    await Error("journal_lines_immutable", () => Task.FromResult(db.SaveChanges()));
});
Test("BR07: EF persistence rejects modifying posted journal content", async f =>
{
    var posted = await f.Post();
    using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
        .UseSqlServer("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true").Options);
    var journal = await f.Journal(posted.ResourceId); db.Attach(journal);
    db.Entry(journal).Property(x => x.Description).CurrentValue = "تجاوز مباشر";
    await Error("posted_journal_immutable", () => db.SaveChangesAsync());
});
Test("BR09: EF persistence rejects audit deletion", async f =>
{
    using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
        .UseSqlServer("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true").Options);
    db.Remove(f.Store.All<AuditEvent>().First());
    await Error("audit_immutable", () => db.SaveChangesAsync());
});
Test("Workflow: return requires reason and restores editable draft", async f =>
{
    var submitted = await f.Action(await f.Draft(), "submit");
    await Error("invalid_text", () => f.Action(submitted, "return", f.Approver));
    var returned = await f.Action(submitted, "return", f.Approver, reason: "تصحيح البيان");
    Assert(returned.Status == "Draft");
    await f.Service.Journal.EditJournal(f.CompanyId, returned.ResourceId, f.Clerk, Fixture.Key(), new(returned.Version, "تصحيح", f.Request().Lines), default);
});
tests.Add(("Wire contract: exact decimal strings and date-only JSON roundtrip", () =>
{
    var input = new CreateExchangeRateRequest("USD", new(2026, 10, 20), 3.1234567890m, "اختبار");
    var json = JsonSerializer.Serialize(input, JsonDefaults.Options);
    Assert(json.Contains("\"rateToFunctional\":\"3.123456789\"") && json.Contains("2026-10-20"));
    Assert(JsonSerializer.Deserialize<CreateExchangeRateRequest>(json, JsonDefaults.Options) == input);
    return Task.CompletedTask;
}));
tests.Add(("Wire contract: excessive decimal precision and exponents are rejected without rounding", () =>
{
    foreach (var input in new[] { "\"1.00000000000000000000000000001\"", "1.00000000000000000000000000001", "1e2", "\"1,000\"" })
    {
        var rejected = false;
        try { JsonSerializer.Deserialize<decimal>(input, JsonDefaults.Options); }
        catch (JsonException) { rejected = true; }
        Assert(rejected, "Input silently rounded or accepted: " + input);
    }
    Assert(JsonSerializer.Deserialize<decimal>("\"0001.2500\"", JsonDefaults.Options) == 1.25m);
    return Task.CompletedTask;
}));
tests.Add(("SQL model: composite company foreign keys and unique idempotency/number indexes", () =>
{
    using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
        .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
    var line = db.Model.FindEntityType(typeof(JournalLine))!;
    Assert(line.GetForeignKeys().Where(k => k.PrincipalEntityType.ClrType != typeof(Company)).All(k => k.Properties.Any(p => p.Name == "CompanyId")));
    var journal = db.Model.FindEntityType(typeof(JournalEntry))!;
    Assert(journal.GetIndexes().Any(x => x.IsUnique && x.Properties.Any(p => p.Name == "Number")));
    var sql = db.Database.GenerateCreateScript();
    Assert(sql.Contains("CK_Line_Amounts") && sql.Contains("IdempotencyRecords") && sql.Contains("decimal(20,10)"));
    return Task.CompletedTask;
}));
tests.Add(("API: every ERP endpoint requires authentication and carries screen trace tags", async () =>
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
    builder.Services.AddSingleton<IErpStore, MemoryStore>();
    builder.Services.AddSingleton<ITransactionRunner>(sp => (MemoryStore)sp.GetRequiredService<IErpStore>());
    builder.Services.AddSingleton<IClock, FixedClock>(); builder.Services.AddErpApplication();
    await using var app = builder.Build(); app.MapErp();
    var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).ToArray();
    Assert(endpoints.Length >= 30);
    Assert(endpoints.All(x => x.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0));
    Assert(endpoints.All(x => x.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.ITagsMetadata>()?.Tags.Count > 0));
    Assert(endpoints.All(x => x.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>().Any(m => m.StatusCode == 200 && m.Type is not null)));
    var h = new DefaultHttpContext();
    await Error("authenticated_subject_required", () => Task.FromResult(ApiSupport.Actor(h)));
    h.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "valid-user"), new Claim("roles", "erp.platform-admin") }, "test", "sub", "roles"));
    Assert(ApiSupport.Actor(h).IsPlatformAdministrator);
}));

AuditTests.Register(Test);
ServiceCompositionTests.Register(Test);
CompanyTests.Register(Test);
BranchTests.Register(Test);
FiscalCalendarTests.Register(Test);
RateAndCloseTests.Register(Test);
UnitTests.Register(Test);
ItemGroupTests.Register(Test);
WarehouseTests.Register(Test);
InventoryIdentityTests.Register(Test);
InventoryOpeningTests.Register(Test);
InventoryOpeningPostingTests.Register(Test);
InventoryOpeningReversalTests.Register(Test);
InventoryReportTests.Register(Test);
JournalProcessingTests.Register(Test);
JournalImpactPreviewTests.Register(Test);
GeneralOpeningTests.Register(Test);
GeneralOpeningReversalTests.Register(Test);
DraftJournalHeaderTests.Register(Test);
JournalHeaderFieldsTests.Register(Test);
JournalLineGuardAcceptance.Register(Test);
JournalReviewHistoryAcceptance.Register(Test);
ItemTests.Register(Test);
ItemGroupUsageTests.Register(Test);
CostCenterTests.Register(Test);
CurrencyTests.Register(Test);
AccountTests.Register(Test);
RecurringTests.Register(Test);
ScheduledReversalTests.Register(Test);
JournalAuditTests.Register(Test);
NumberingTests.Register(Test);
FinancialMappingTests.Register(Test);
JournalDocumentTypeFoundationTests.Register(Test);
ApprovalRouteFoundationTests.Register(Test);
SecurityRoleFoundationTests.Register(Test);
tests.Add(("HTTP: authentication boundary, journal lifecycle, replay, errors and exact JSON", HttpIntegration.Run));
tests.Add(("OpenAPI: production routes expose optional draft headers without SQL dependency", OpenApiSnapshotTests.Run));
if (args.Contains("--sql", StringComparer.Ordinal)) tests.Add(("SQL Server integration", SqlIntegration.Run));
else Console.WriteLine("SKIP live SQL Server integration (run explicitly with --sql and AMEEN_TEST_SQL_CONNECTION).");

var failures = 0; var started = DateTimeOffset.UtcNow;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + "\n" + ex); }
}
Console.WriteLine($"RESULT: {tests.Count - failures}/{tests.Count} passed; {failures} failed; {(DateTimeOffset.UtcNow - started).TotalSeconds:F2}s");
Console.WriteLine(args.Contains("--sql", StringComparer.Ordinal)
    ? "Scope: domain/application, HTTP, live SQL integration and production HTTPS/JWT/OIDC/OpenAPI pipeline."
    : "Scope: domain/application, SQL model and test HTTP host. Run --sql for live SQL and production-host integration.");
return failures == 0 ? 0 : 1;
