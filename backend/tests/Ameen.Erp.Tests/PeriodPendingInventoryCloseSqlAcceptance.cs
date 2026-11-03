using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL11-T02/C01 bounded negative: an incomplete INV06 opening must not
// be bypassed by GL close. This is not an all-ledger reconciliation.
public static class PeriodPendingInventoryCloseSqlAcceptance
{
    public static async Task Run(string connection, Guid cid, Guid branch,
        Actor owner, Actor writer, Actor approver)
    {
        ErpDbContext Db() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Db();
            return await work(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        // Dedicated test year: no alteration of another suite's periods.
        var year = await Call(s => s.FiscalCalendar.CreateFiscalYear(cid, owner,
            Fixture.Key(), new("GL11-INV-2049",
                new DateOnly(2049, 1, 1), new DateOnly(2049, 12, 31)), default));
        FiscalPeriod period;
        await using (var db = Db())
            period = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid &&
                    x.FiscalYearId == year.ResourceId &&
                    x.StartsOn == new DateOnly(2049, 1, 1));

        var sales = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("GL11-INV-S", "مبيعات اختبار إقفال", AccountType.Revenue,
                null, true, false, false, null, NormalBalance.Credit), default));
        var stock = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("GL11-INV-I", "مخزون اختبار إقفال", AccountType.Asset,
                null, true, true, false, null, NormalBalance.Debit), default));
        var cost = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("GL11-INV-C", "تكلفة اختبار إقفال", AccountType.Expense,
                null, true, false, false, null, NormalBalance.Debit), default));
        var group = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(),
            new("GL11-INV-G", "مجموعة اختبار إقفال", sales.ResourceId,
                InventoryAccountId: stock.ResourceId, CostAccountId: cost.ResourceId),
            default));
        var unit = await Call(s => s.Unit.CreateUnit(cid, owner, Fixture.Key(),
            new("GL11-INV-U", "وحدة اختبار إقفال", "U49", 0, "وحدة"), default));
        var item = await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(),
            new("GL11-INV-ITEM", "صنف اختبار إقفال", ItemType.Goods,
                ItemTracking.None, group.ResourceId, unit.ResourceId,
                new[] { new ItemUnitRequest(unit.ResourceId, 1, null, null) }),
            default));
        Guid keeper;
        await using (var db = Db())
            keeper = (await db.Set<Membership>().SingleAsync(x =>
                x.CompanyId == cid && x.Subject == owner.Subject)).Id;
        var warehouse = await Call(s => s.Warehouse.CreateWarehouse(cid, owner,
            Fixture.Key(), new("GL11-INV-WH", "مخزن اختبار إقفال",
                branch, keeper, stock.ResourceId, WarehouseType.Standard,
                false, Array.Empty<WarehouseLocationRequest>()), default));

        var date = new DateOnly(2049, 1, 10);
        var opening = await Call(s => s.InventoryOpening.Create(cid, writer,
            Fixture.Key(), new CreateInventoryOpeningRequest(branch,
                warehouse.ResourceId, date, date, date, "SAR", 1, "GL11-COUNT",
                "افتتاح مخزون غير مكتمل", new[]
                {
                    new InventoryOpeningLineRequest(item.ResourceId, unit.ResourceId, 2, 5)
                }), default));
        Ensure(opening.Status == "Draft",
            "GL11-T02 requires one draft inventory opening");

        async Task<(Guid Version, bool Closed, int Openings, int Journals,
            int Audit, int Receipts, int Outbox, long Numbers)> Snapshot()
        {
            await using var db = Db();
            var actual = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            return (
                actual.Version, actual.IsClosed,
                await db.Set<InventoryOpening>().CountAsync(x => x.CompanyId == cid),
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid)
                    .SumAsync(x => x.LastNumber));
        }

        var before = await Snapshot();
        var preview = await Call(s => s.PeriodClose.PreviewPeriodClose(cid,
            period.Id, approver, 0, 10, default));
        Ensure(preview.GeneralLedgerOnly && !preview.CanCloseGeneralLedger &&
            preview.PendingInventoryOpeningCount == 1 &&
            preview.UnpostedJournalCount == 0 &&
            !preview.Checks.Single(x => x.Code == "no_pending_inventory_openings").Passed &&
            preview.Checks.Single(x => x.Code == "inventory_openings_reconciled").Passed,
            "GL11-T02 did not preview the pending inventory opening");
        Ensure(before == await Snapshot(),
            "GL11-T02 preview wrote financial SQL");

        var key = Fixture.Key();
        var body = new ChangePeriodRequest(period.Version, "إقفال بعد معالجة المخزون");
        try
        {
            await Call(s => s.PeriodClose.ChangePeriod(cid, period.Id,
                approver, key, body, true, default));
            throw new Exception("GL11-T02 closed period with draft inventory opening");
        }
        catch (BusinessException ex) when (ex.Code == "pending_inventory_openings") { }
        Ensure(before == await Snapshot(),
            "GL11-T02 rejected close changed period, inventory, journal, audit, receipt, outbox or number");
        await using (var db = Db())
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x =>
                x.CompanyId == cid && x.Key == key),
                "GL11-T02 denied close consumed its idempotency key");

        var cancelled = await Call(s => s.InventoryOpening.Transition(cid,
            opening.ResourceId, writer, Fixture.Key(),
            new TransitionRequest(opening.Version, "إلغاء افتتاح مخزون معلّق"),
            "cancel", default));
        Ensure(cancelled.Status == "Cancelled",
            "GL11-T02 official inventory cancellation did not complete");
        var ready = await Call(s => s.PeriodClose.PreviewPeriodClose(cid,
            period.Id, approver, 0, 10, default));
        Ensure(ready.CanCloseGeneralLedger &&
            ready.PendingInventoryOpeningCount == 0 &&
            ready.Checks.All(x => x.Passed),
            "GL11-T02 cancelled opening still blocks period close");

        var closed = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, key, body, true, default));
        var replay = await Call(s => s.PeriodClose.ChangePeriod(cid,
            period.Id, approver, key, body, true, default));
        Ensure(closed.Status == "Closed" && closed == replay,
            "GL11-T02 repaired period did not close exactly once");
        await using (var db = Db())
        {
            var saved = await db.Set<FiscalPeriod>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == period.Id);
            var openingSaved = await db.Set<InventoryOpening>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == opening.ResourceId);
            Ensure(saved.IsClosed && saved.Version == closed.Version &&
                openingSaved.State == InventoryOpeningState.Cancelled &&
                openingSaved.Version == cancelled.Version &&
                !await db.Set<JournalEntry>().AnyAsync(x =>
                    x.CompanyId == cid && x.FiscalPeriodId == period.Id) &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                    x.ResourceId == period.Id && x.Action == "period.closed") == 1 &&
                await db.Set<IdempotencyRecord>().CountAsync(x =>
                    x.CompanyId == cid && x.Key == key) == 1,
                "GL11-T02 repaired inventory close created journals or duplicate audit/receipts");
        }
        Console.WriteLine("  SQL PASS GL11-T02 pending INV06 opening blocks close atomically; cancelled opening permits one audited close/replay");
    }
    private static void Ensure(bool ok, string reason)
    {
        if (!ok) throw new Exception(reason);
    }
}
