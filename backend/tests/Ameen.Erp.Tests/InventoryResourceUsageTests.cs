using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class InventoryResourceUsageTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV01/INV04/INV05-OP03: posted opening appears in warehouse item and lot usage and movement pages", async f =>
        {
            var (request, approved) = await InventoryOpeningPostingTests.Setup(f, ItemTracking.Lot);
            var line = request.Lines.Single();
            var lotId = line.IdentityId!.Value;
            var before = await f.Service.InventoryIdentity.GetLotUsage(f.CompanyId, line.ItemId, lotId, f.Owner, default);
            Ensure(before.MovementCount == 0 && before.OpeningDocumentCount == 0 && !before.HasStockMovements);
            await InventoryOpeningPostingTests.Post(f, approved, await InventoryOpeningPostingTests.Plan(f, approved));
            var audit = f.Store.Count<AuditEvent>();
            var outbox = f.Store.Count<OutboxEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var itemUsage = await f.Service.Item.GetItemUsage(f.CompanyId, line.ItemId, f.Owner, default);
            var warehouseUsage = await f.Service.Warehouse.GetWarehouseUsage(f.CompanyId, request.WarehouseId, f.Owner, default);
            var lotUsage = await f.Service.InventoryIdentity.GetLotUsage(f.CompanyId, line.ItemId, lotId, f.Owner, default);
            foreach (var usage in new[] { itemUsage, warehouseUsage, lotUsage })
                Ensure(usage.MovementCount == 1 && usage.OpeningDocumentCount == 1 &&
                    usage.ReversalMovementCount == 0 && usage.HasStockMovements);
            var byItem = await f.Service.Item.ListItemMovements(f.CompanyId, line.ItemId, f.Owner, 0, 10, default);
            var byWarehouse = await f.Service.Warehouse.ListWarehouseMovements(f.CompanyId, request.WarehouseId, f.Owner, 0, 10, default);
            var byLot = await f.Service.InventoryIdentity.ListLotMovements(f.CompanyId, line.ItemId, lotId, f.Owner, 0, 10, default);
            Ensure(byItem.TotalCount == 1 && byWarehouse.TotalCount == 1 && byLot.TotalCount == 1 &&
                byItem.Items[0].Id == byWarehouse.Items[0].Id && byItem.Items[0].Id == byLot.Items[0].Id &&
                byLot.Items[0].OpeningId == approved.ResourceId && byLot.Items[0].IdentityId == lotId &&
                byLot.Items[0].Value == 10.5m && byLot.Items[0].BaseQuantity == 2m);
            Ensure(f.Store.Count<AuditEvent>() == audit && f.Store.Count<OutboxEvent>() == outbox &&
                f.Store.Count<IdempotencyRecord>() == receipts);
        });
        test("INV01/INV04/INV05-OP03: usage filters foreign branch, missing resource, tracking and paging", async f =>
        {
            var (request, approved) = await InventoryOpeningPostingTests.Setup(f, ItemTracking.Serial);
            await InventoryOpeningPostingTests.Post(f, approved, await InventoryOpeningPostingTests.Plan(f, approved));
            var line = request.Lines.Single();
            var serialId = line.IdentityId!.Value;
            var foreignBranchReader = new Actor("other-branch-usage-reader");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(),
                new(foreignBranchReader.Subject, new[] { Permissions.Read }, false, new[] { f.OtherBranchId }, null), default);
            var record = await f.Service.InventoryIdentity.GetSerialUsage(f.CompanyId, line.ItemId, serialId, f.Owner, default);
            Ensure(record.MovementCount == 1 && record.OpeningDocumentCount == 1);
            var isolatedItem = await f.Service.Item.GetItemUsage(f.CompanyId, line.ItemId, foreignBranchReader, default);
            var isolatedSerial = await f.Service.InventoryIdentity.GetSerialUsage(f.CompanyId, line.ItemId, serialId,
                foreignBranchReader, default);
            Ensure(isolatedItem.MovementCount == 0 && isolatedSerial.MovementCount == 0 &&
                (await f.Service.InventoryIdentity.ListSerialMovements(f.CompanyId, line.ItemId, serialId,
                    foreignBranchReader, 0, 10, default)).TotalCount == 0 &&
                (await f.Service.Item.ListItemMovements(f.CompanyId, line.ItemId,
                    foreignBranchReader, 0, 10, default)).TotalCount == 0);
            await Error("branch_access_denied", () => f.Service.Warehouse.GetWarehouseUsage(f.CompanyId,
                request.WarehouseId, foreignBranchReader, default));
            await Error("branch_access_denied", () => f.Service.Warehouse.ListWarehouseMovements(f.CompanyId,
                request.WarehouseId, foreignBranchReader, 0, 10, default));
            await Error("reference_not_found", () => f.Service.InventoryIdentity.GetLotUsage(f.CompanyId, line.ItemId, serialId,
                f.Owner, default));
            await Error("reference_not_found", () => f.Service.Item.GetItemUsage(f.CompanyId, Guid.NewGuid(),
                f.Owner, default));
            await Error("permission_denied", () => f.Service.InventoryIdentity.GetSerialUsage(f.CompanyId,
                line.ItemId, serialId, new Actor("outside-account"), default));
            await Error("invalid_pagination", () => f.Service.InventoryIdentity.ListSerialMovements(f.CompanyId,
                line.ItemId, serialId, f.Owner, -1, 10, default));
            Ensure((await f.Service.InventoryIdentity.ListSerialMovements(f.CompanyId, line.ItemId, serialId,
                f.Owner, 1, 10, default)).TotalCount == 1);
        });
    }

    private static void Ensure(bool value)
    {
        if (!value) throw new Exception("Inventory reference usage acceptance failed.");
    }

    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected inventory reference usage rejection: " + code);
    }
}
