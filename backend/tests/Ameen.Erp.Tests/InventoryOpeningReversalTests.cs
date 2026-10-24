using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class InventoryOpeningReversalTests
{
    private static Actor Writer => InventoryOpeningTests.Writer;
    private static async Task<CommandReceipt> Setup(Fixture f, ItemTracking tracking = ItemTracking.None)
    {
        var (_, r) = await InventoryOpeningPostingTests.Setup(f, tracking);
        var posted = await InventoryOpeningPostingTests.Post(f, r, await InventoryOpeningPostingTests.Plan(f, r));
        var member = f.Store.All<Membership>().Single(x => x.Subject == Writer.Subject);
        await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(Writer.Subject,
            member.GrantedPermissions.Append(Permissions.InventoryReverse).ToArray(), true, Array.Empty<Guid>(), member.Version), default);
        return posted;
    }
    private static Task<CommandReceipt> Create(Fixture f, CommandReceipt source, DateOnly? date = null, string? key = null) =>
        f.Service.InventoryOpening.CreateReversal(f.CompanyId, source.ResourceId, Writer, key ?? Fixture.Key(), new(source.Version, date ?? Fixture.Date, "تصحيح الافتتاح"), default);
    private static async Task<CommandReceipt> Approve(Fixture f, CommandReceipt r)
    {
        r = await InventoryOpeningTests.Action(f, r, "submit");
        return await InventoryOpeningTests.Action(f, r, "approve", new Actor("stock-reviewer"));
    }
    private static Task<CommandReceipt> Post(Fixture f, CommandReceipt r, string? key = null) =>
        f.Service.InventoryOpening.PostReversal(f.CompanyId, r.ResourceId, Writer, key ?? Fixture.Key(), new(r.Version), default);
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV06 reversal: approved source reversal preserves history and nets stock and ledger to zero", async f =>
        {
            var source = await Setup(f, ItemTracking.Serial); var draft = await Create(f, source);
            var card = await InventoryOpeningTests.Get(f, draft.ResourceId);
            Ensure(card.ReversesOpeningId == source.ResourceId && card.Lines.Single().SerialNumber == "ID-1");
            var approved = await Approve(f, draft); var preview = await f.Service.InventoryOpening.Preview(f.CompanyId, approved.ResourceId, Writer, default);
            Ensure(preview.CanPost && preview.TotalValue == -5.25m);
            var key = Fixture.Key(); var posted = await Post(f, approved, key); Ensure(posted == await Post(f, approved, key));
            var movements = f.Store.All<StockMovement>().ToArray(); var original = movements.Single(x => x.ReversesMovementId is null); var inverse = movements.Single(x => x.ReversesMovementId != null);
            Ensure(movements.Length == 2 && inverse.ReversesMovementId == original.Id && movements.Sum(x => x.BaseQuantity) == 0 && movements.Sum(x => x.Value) == 0);
            var journals = f.Store.All<JournalEntry>().ToArray(); Ensure(journals.Length == 2 && journals.Single(x => x.Id == original.JournalId).State == JournalState.Reversed);
            Ensure(journals.SelectMany(x => x.Lines).Where(x => x.AccountId == original.InventoryAccountId).Sum(x => x.FunctionalDebit - x.FunctionalCredit) == 0);
            var period = f.Store.All<FiscalPeriod>().Single(x => x.Id == card.FiscalPeriodId);
            Ensure((await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, period.Id, f.Approver, 0, 20, default)).CanCloseGeneralLedger);
        });
        test("INV06 reversal: one live reversal and cancelled draft releases reservation", async f =>
        {
            var source = await Setup(f); var key = Fixture.Key(); var draft = await Create(f, source, key: key);
            Ensure(draft == await Create(f, source, key: key));
            await Error("opening_reversal_exists", () => Create(f, source));
            await InventoryOpeningTests.Action(f, draft, "cancel");
            var other = await Create(f, source); Ensure(other.ResourceId != draft.ResourceId);
            Ensure((await f.Service.InventoryOpening.Reversals(f.CompanyId, source.ResourceId, Writer, 0, 1, default)).TotalCount == 2);
        });
        test("INV06 reversal: dates permissions state and separate approver enforced", async f =>
        {
            var source = await Setup(f);
            await Error("reversal_before_original", () => Create(f, source, Fixture.Date.AddDays(-1)));
            await Error("permission_denied", () => f.Service.InventoryOpening.CreateReversal(f.CompanyId, source.ResourceId, f.Clerk, Fixture.Key(), new(source.Version, Fixture.Date, "عكس"), default));
            var draft = await Create(f, source); await Error("opening_state_conflict", () => Post(f, draft));
            draft = await InventoryOpeningTests.Action(f, draft, "submit");
            await Error("self_approval_forbidden", () => InventoryOpeningTests.Action(f, draft, "approve"));
        });
        test("INV06 reversal: return correct reason and resubmit preserves immutable quantities", async f =>
        {
            var source = await Setup(f); var draft = await Create(f, source);
            var card = await InventoryOpeningTests.Get(f, draft.ResourceId);
            await Error("opening_reversal_immutable", () => f.Service.InventoryOpening.Edit(f.CompanyId, draft.ResourceId, Writer, Fixture.Key(), new(draft.Version, "تعديل", Array.Empty<InventoryOpeningLineRequest>(), "سبب"), default));
            draft = await InventoryOpeningTests.Action(f, draft, "submit"); draft = await InventoryOpeningTests.Action(f, draft, "return", new Actor("stock-reviewer"));
            draft = await f.Service.InventoryOpening.CorrectReversal(f.CompanyId, draft.ResourceId, Writer, Fixture.Key(), new(draft.Version, "سبب مصحح"), default);
            Ensure((await InventoryOpeningTests.Get(f, draft.ResourceId)).Lines.Single().Value == card.Lines.Single().Value);
            await Post(f, await Approve(f, draft));
        });
        test("INV06 reversal: failed post restores source links movement outbox and approval", async f =>
        {
            var source = await Setup(f); var approved = await Approve(f, await Create(f, source)); var key = Fixture.Key();
            var audits = f.Store.Count<AuditEvent>(); f.Store.FailBeforeCommit = true;
            try { await Post(f, approved, key); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure(f.Store.Count<StockMovement>() == 1 && f.Store.Count<JournalEntry>() == 1 && f.Store.Count<OutboxEvent>() == 2 && f.Store.Count<AuditEvent>() == audits);
            Ensure(f.Store.All<JournalEntry>().Single().State == JournalState.Posted && f.Store.All<JournalEntry>().Single().ReversalJournalId is null);
            Ensure((await InventoryOpeningTests.Get(f, approved.ResourceId)).State == InventoryOpeningState.Approved);
            await Post(f, approved, key);
        });
        test("INV06 reversal: later open period reverses closed period with historical accounts despite settings change", async f =>
        {
            var source = await Setup(f); var card = await InventoryOpeningTests.Get(f, source.ResourceId);
            var period = f.Store.All<FiscalPeriod>().Single(x => x.Id == card.FiscalPeriodId);
            await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default);
            await Error("period_closed", () => Create(f, source));
            var settings = await f.Service.InventoryOpening.GetPostingSettings(f.CompanyId, f.Owner, default);
            await f.Service.InventoryOpening.ConfigurePosting(f.CompanyId, f.Owner, Fixture.Key(), new(settings!.Version, f.CashId, "تغيير"), default);
            var draft = await Create(f, source, new(2026, 11, 1)); await Post(f, await Approve(f, draft));
            var reverse = await InventoryOpeningTests.Get(f, draft.ResourceId); Ensure(reverse.ClearingAccountId == card.ClearingAccountId);
            var next = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn == new DateOnly(2026, 11, 1));
            Ensure((await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, next.Id, f.Approver, 0, 10, default)).CanCloseGeneralLedger);
        });
        test("INV06 reversal: no reverse of reversal and no second posted reversal", async f =>
        {
            var source = await Setup(f); var approved = await Approve(f, await Create(f, source)); var posted = await Post(f, approved);
            await Error("opening_not_reversible", () => Create(f, posted));
            await Error("opening_already_reversed", () => Create(f, source));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Opening reversal assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
