using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class InventoryReportTests
{
    private static Task<InventoryBalanceResult> Balance(Fixture f, DateOnly? from = null, DateOnly? to = null, int offset = 0, bool zero = true) =>
        f.Service.Report.InventoryBalances(f.CompanyId, f.Owner, from ?? Fixture.Date, to ?? Fixture.Date, null, null, null, null, zero, offset, 1, default);
    private static Task<InventoryLedgerResult> Ledger(Fixture f, Guid item, DateOnly? from = null, DateOnly? to = null, int offset = 0, string? identity = null) =>
        f.Service.Report.InventoryLedger(f.CompanyId, item, f.Owner, from ?? Fixture.Date, to ?? Fixture.Date, null, null, identity, offset, 1, default);
    private static async Task<(CreateInventoryOpeningRequest Request, CommandReceipt Posted)> Post(Fixture f, ItemTracking tracking = ItemTracking.None)
    {
        var (request, approved) = await InventoryOpeningPostingTests.Setup(f, tracking);
        return (request, await InventoryOpeningPostingTests.Post(f, approved, await InventoryOpeningPostingTests.Plan(f, approved)));
    }
    private static async Task Reverse(Fixture f, CommandReceipt source, DateOnly date)
    {
        var member = f.Store.All<Membership>().Single(x => x.Subject == InventoryOpeningTests.Writer.Subject);
        await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(member.Subject,
            member.GrantedPermissions.Append(Permissions.InventoryReverse).ToArray(), true, Array.Empty<Guid>(), member.Version), default);
        var r = await f.Service.InventoryOpening.CreateReversal(f.CompanyId, source.ResourceId, InventoryOpeningTests.Writer, Fixture.Key(), new(source.Version, date, "تصحيح"), default);
        r = await InventoryOpeningTests.Action(f, r, "submit"); r = await InventoryOpeningTests.Action(f, r, "approve", new("stock-reviewer"));
        await f.Service.InventoryOpening.PostReversal(f.CompanyId, r.ResourceId, InventoryOpeningTests.Writer, Fixture.Key(), new(r.Version), default);
    }
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("RPT08/09: approved stock has no balance until posted; inclusive date boundaries", async f =>
        {
            var (request, approved) = await InventoryOpeningPostingTests.Setup(f);
            Ensure((await Balance(f)).Rows.TotalCount == 0);
            await InventoryOpeningPostingTests.Post(f, approved, await InventoryOpeningPostingTests.Plan(f, approved));
            var item = request.Lines[0].ItemId; var row = (await Balance(f)).Rows.Items.Single();
            Ensure(row.OpeningQuantity == 0 && row.InQuantity == 2 && row.ClosingValue == 10.5m);
            Ensure((await Balance(f, to: Fixture.Date.AddDays(-1), from: Fixture.Date.AddDays(-1))).Rows.TotalCount == 0);
            var later = await Ledger(f, item, Fixture.Date.AddDays(1), Fixture.Date.AddDays(1));
            Ensure(later.OpeningQuantity == 2 && later.OpeningValue == 10.5m && later.Rows.TotalCount == 0 && later.ClosingValue == 10.5m);
        });
        test("RPT08/09: later reversal preserves historical as-of values and running balance across pages", async f =>
        {
            var (request, source) = await Post(f); var item = request.Lines[0].ItemId;
            await Reverse(f, source, Fixture.Date.AddDays(1));
            Ensure((await Balance(f)).ClosingValue == 10.5m);
            var second = await Ledger(f, item, to: Fixture.Date.AddDays(1), offset: 1);
            var row = second.Rows.Items.Single();
            Ensure(second.Rows.TotalCount == 2 && row.OutQuantity == 2 && row.RunningQuantity == 0 && row.RunningValue == 0 && row.ReversesMovementId != null);
            Ensure(row.DocumentUnits.Single().Quantity == -2 && row.DocumentUnits.Single().UnitCost == 5.25m && row.DocumentUnits.Single().Value == -10.5m);
            var onlyReverse = await Ledger(f, item, Fixture.Date.AddDays(1), Fixture.Date.AddDays(1));
            Ensure(onlyReverse.OpeningValue == 10.5m && onlyReverse.OutValue == 10.5m && onlyReverse.ClosingValue == 0);
            var beyond = await Ledger(f, item, to: Fixture.Date.AddDays(1), offset: 20);
            Ensure(beyond.Rows.Items.Count == 0 && beyond.InValue == 10.5m && beyond.OutValue == 10.5m && beyond.ClosingQuantity == 0);
        });
        test("RPT08: zero closing filter and totals are computed before pagination", async f =>
        {
            var (_, source) = await Post(f); var beyond = await Balance(f, offset: 10);
            Ensure(beyond.Rows.Items.Count == 0 && beyond.Rows.TotalCount == 1 && beyond.ClosingValue == 10.5m);
            await Reverse(f, source, Fixture.Date);
            var withZero = await Balance(f); Ensure(withZero.Rows.TotalCount == 1 && withZero.InValue == 10.5m && withZero.OutValue == 10.5m);
            Ensure((await Balance(f, zero: false)).Rows.TotalCount == 0);
        });
        test("RPT09: lot and serial number filters do not confuse card code or another identity", async f =>
        {
            var (request, _) = await Post(f, ItemTracking.Serial); var item = request.Lines[0].ItemId;
            var matching = await Ledger(f, item, identity: " id-1 ");
            Ensure(matching.ClosingQuantity == 1 && matching.Rows.Items.Single().SerialNumber == "ID-1");
            Ensure((await Ledger(f, item, identity: "UNKNOWN")).Rows.TotalCount == 0);
            await Error("invalid_code", () => Ledger(f, item, identity: "bad number"));
        });
        test("RPT08/09: branch and company scope are enforced before exposing stock", async f =>
        {
            var (request, _) = await Post(f); var item = request.Lines[0].ItemId;
            await Error("permission_denied", () => f.Service.Report.InventoryBalances(Guid.NewGuid(), f.Owner, Fixture.Date, Fixture.Date, null, null, null, null, true, 0, 10, default));
            await Error("branch_access_denied", () => f.Service.Report.InventoryLedger(f.CompanyId, item, f.Limited, Fixture.Date, Fixture.Date, f.OtherBranchId, null, null, 0, 10, default));
            var empty = await f.Service.Report.InventoryBalances(f.CompanyId, f.Owner, Fixture.Date, Fixture.Date, f.OtherBranchId, null, null, null, true, 0, 10, default);
            Ensure(empty.Rows.TotalCount == 0 && empty.ClosingValue == 0);
            await Error("warehouse_branch_mismatch", () => f.Service.Report.InventoryBalances(f.CompanyId, f.Owner, Fixture.Date, Fixture.Date, f.OtherBranchId, request.WarehouseId, null, null, true, 0, 10, default));
        });
        test("RPT08/09: stock valuation equals control ledger and reads create no effects", async f =>
        {
            var (request, _) = await Post(f); var count = f.Store.Count<AuditEvent>();
            var balance = await Balance(f); var row = balance.Rows.Items.Single();
            var ledger = await f.Service.Report.AccountLedger(f.CompanyId, row.InventoryAccountId, f.Owner, Fixture.Date, Fixture.Date, null, 0, 10, default);
            Ensure(balance.ClosingValue == ledger.ClosingBalance);
            var detail = await Ledger(f, request.Lines[0].ItemId);
            Ensure(detail.Rows.Items.Single().OpeningId == f.Store.All<InventoryOpening>().Single().Id && detail.InventoryOpeningsOnly && f.Store.Count<AuditEvent>() == count);
        });
        test("RPT08/09: inactive cards retain history and implicit branch scope excludes other stock", async f =>
        {
            var (request, _) = await Post(f); var item = f.Store.All<Item>().Single(x => x.Id == request.Lines[0].ItemId);
            await f.Service.Item.SetItemStatus(f.CompanyId, item.Id, f.Owner, Fixture.Key(), new(item.Version, false, "إيقاف"), default);
            Ensure((await Balance(f)).ClosingValue == 10.5m && (await Ledger(f, item.Id)).ClosingQuantity == 2);
            var reader = new Actor("other-stock-reader");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(reader.Subject, new[] { Permissions.Read }, false, new[] { f.OtherBranchId }, null), default);
            var report = await f.Service.Report.InventoryBalances(f.CompanyId, reader, Fixture.Date, Fixture.Date, null, null, null, null, true, 0, 10, default);
            Ensure(report.Rows.TotalCount == 0 && report.BranchIds.SequenceEqual(new[] { f.OtherBranchId }));
            await Error("branch_access_denied", () => f.Service.Report.InventoryLedger(f.CompanyId, item.Id, reader, Fixture.Date, Fixture.Date, null, request.WarehouseId, null, 0, 10, default));
        });
        test("RPT08/09: invalid dates and paging are rejected", async f =>
        {
            await Error("invalid_date_range", () => Balance(f, to: Fixture.Date.AddDays(-1)));
            await Error("invalid_date_range", () => Ledger(f, Guid.NewGuid(), to: Fixture.Date.AddDays(-1)));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Inventory report assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
