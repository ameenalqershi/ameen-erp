using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// Structural-only acceptance for existing Notion V1 field groups FG01.
public static class InventoryDocumentFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV07/08/09/10/13/14-FG01: company branch fiscal-year scope fields", async f =>
        {
            var year = f.Store.All<FiscalYear>().Single().Id;
            var receipt = new InventoryReceiptHeader(f.CompanyId, f.BranchId, year);
            var issue = new InventoryIssueHeader(f.CompanyId, f.BranchId, year);
            var transfer = new InventoryTransferHeader(f.CompanyId, f.BranchId, year);
            var transferReceipt = new InventoryTransferReceiptHeader(f.CompanyId, f.BranchId, year);
            var adjustment = new InventoryAdjustmentHeader(f.CompanyId, f.BranchId, year);
            var damage = new InventoryDamageHeader(f.CompanyId, f.BranchId, year);
            Ensure(new CompanyEntity[] { receipt, issue, transfer, transferReceipt, adjustment, damage }
                .All(x => x.CompanyId == f.CompanyId && x.Id != Guid.Empty && x.Version != Guid.Empty),
                "INV document FG01 generated scope mismatch");
            Ensure(receipt.BranchId == f.BranchId && receipt.FiscalYearId == year &&
                issue.BranchId == f.BranchId && issue.FiscalYearId == year &&
                transfer.BranchId == f.BranchId && transfer.FiscalYearId == year &&
                transferReceipt.BranchId == f.BranchId && transferReceipt.FiscalYearId == year &&
                adjustment.BranchId == f.BranchId && adjustment.FiscalYearId == year &&
                damage.BranchId == f.BranchId && damage.FiscalYearId == year,
                "INV document FG01 branch/year field mismatch");
            Ensure(f.Store.Count<StockMovement>() == 0 && f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "INV document FG01 created financial effects");
        });
        test("INV12-FG01: optional branch, opaque status code and tenant warehouse reference", async f =>
        {
            var request = await WarehouseTests.Setup(f);
            var warehouse = await f.Service.Warehouse.CreateWarehouse(f.CompanyId, f.Owner,
                Fixture.Key(), request, default);
            var header = new InventoryCountHeader(f.CompanyId, null, "TEST_STATUS", warehouse.ResourceId);
            Ensure(header.CompanyId == f.CompanyId && header.Id != Guid.Empty &&
                header.Version != Guid.Empty && header.BranchId is null &&
                header.StatusCode == "TEST_STATUS" && header.WarehouseId == warehouse.ResourceId,
                "INV12 FG01 field mapping mismatch");
            var scoped = new InventoryCountHeader(f.CompanyId, f.BranchId, "TEST_STATUS", warehouse.ResourceId);
            Ensure(scoped.BranchId == f.BranchId, "INV12 optional branch value lost");
            Ensure(f.Store.Count<StockMovement>() == 0 && f.Store.Count<JournalEntry>() == 0,
                "INV12 FG01 created stock/ledger effects");
        });
        test("INV15-FG01: optional branch, opaque status and required start date", f =>
        {
            var header = new InventoryRevaluationHeader(f.CompanyId, null, "TEST_STATUS",
                new DateOnly(2026, 1, 1));
            Ensure(header.CompanyId == f.CompanyId && header.Id != Guid.Empty &&
                header.Version != Guid.Empty && header.BranchId is null &&
                header.StatusCode == "TEST_STATUS" && header.FromDate == new DateOnly(2026, 1, 1),
                "INV15 FG01 field mapping mismatch");
            return Task.CompletedTask;
        });
        test("INV document FG01: empty company/branch/year/warehouse/status/date rejected", f =>
        {
            var year = f.Store.All<FiscalYear>().Single().Id;
            Reject("company_required", () => new InventoryReceiptHeader(Guid.Empty, f.BranchId, year));
            Reject("inventory_document_scope_required", () => new InventoryReceiptHeader(f.CompanyId, Guid.Empty, year));
            Reject("inventory_document_scope_required", () => new InventoryReceiptHeader(f.CompanyId, f.BranchId, Guid.Empty));
            Reject("inventory_document_branch_invalid", () => new InventoryCountHeader(f.CompanyId, Guid.Empty, "TEST_STATUS", Guid.NewGuid()));
            Reject("inventory_count_warehouse_required", () => new InventoryCountHeader(f.CompanyId, null, "TEST_STATUS", Guid.Empty));
            Reject("invalid_text", () => new InventoryCountHeader(f.CompanyId, null, " ", Guid.NewGuid()));
            Reject("invalid_code", () => new InventoryCountHeader(f.CompanyId, null, "bad status", Guid.NewGuid()));
            Reject("inventory_revaluation_from_date_required", () => new InventoryRevaluationHeader(f.CompanyId, null, "TEST_STATUS", default));
            Reject("inventory_document_branch_invalid", () => new InventoryRevaluationHeader(f.CompanyId, Guid.Empty, "TEST_STATUS", new DateOnly(2026, 1, 1)));
            Ensure(f.Store.Count<InventoryReceiptHeader>() == 0 && f.Store.Count<InventoryIssueHeader>() == 0 &&
                f.Store.Count<InventoryTransferHeader>() == 0 && f.Store.Count<InventoryTransferReceiptHeader>() == 0 &&
                f.Store.Count<InventoryCountHeader>() == 0 && f.Store.Count<InventoryAdjustmentHeader>() == 0 &&
                f.Store.Count<InventoryDamageHeader>() == 0 && f.Store.Count<InventoryRevaluationHeader>() == 0 &&
                f.Store.Count<StockMovement>() == 0 && f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "INV document FG01 invalid construction persisted an effect");
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("INV document FG01 expected " + code);
    }
    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
