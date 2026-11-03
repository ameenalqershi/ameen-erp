using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CashReceiptAllocationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH03-CTRL01: exact and partial allocations do not exceed remaining", f =>
        {
            CashReceiptAllocation.Validate(100m, 90m, 10m);
            CashReceiptAllocation.Validate(100m, 0.000001m, 0m);
            CashReceiptAllocation.Validate(100m, 75m, 0m);
            return Task.CompletedTask;
        });
        test("CASH03-AC02: repeated invoice lines cannot bypass total allocation limit", f =>
        {
            var invoice = Guid.NewGuid();
            var another = Guid.NewGuid();
            CashReceiptAllocation.ValidateBatch(new[] {
                new CashReceiptAllocationLine(invoice, 100m, 60m, 0m),
                new CashReceiptAllocationLine(invoice, 100m, 30m, 10m),
                new CashReceiptAllocationLine(another, 50m, 50m, 0m)
            });
            RejectBatch("cash_receipt_allocation_exceeds_remaining", new[] {
                new CashReceiptAllocationLine(invoice, 100m, 60m, 0m),
                new CashReceiptAllocationLine(invoice, 100m, 41m, 0m)
            });
            RejectBatch("cash_receipt_remaining_mismatch", new[] {
                new CashReceiptAllocationLine(invoice, 100m, 20m, 0m),
                new CashReceiptAllocationLine(invoice, 110m, 20m, 0m)
            });
            RejectBatch("cash_receipt_invoice_required", new[] {
                new CashReceiptAllocationLine(Guid.Empty, 100m, 10m, 0m)
            });
            return Task.CompletedTask;
        });

        test("CASH03-AC02: excess allocation and settlement discount fail closed", f =>
        {
            Reject("cash_receipt_allocation_exceeds_remaining", 100m, 100m, 0.000001m);
            Reject("cash_receipt_allocation_exceeds_remaining", 100m, 90m, 11m);
            Reject("cash_receipt_allocation_exceeds_remaining", 0m, 1m, 0m);
            Reject("invalid_amount", 100m, -1m, 0m);
            Reject("invalid_amount", 100m, 1m, -1m);
            Reject("invalid_amount", 100m, 0m, 0m);
            Reject("invalid_amount", 100m, 0.0000001m, 0m);
            return Task.CompletedTask;
        });
    }

    private static void RejectBatch(string expected, CashReceiptAllocationLine[] lines)
    {
        try { CashReceiptAllocation.ValidateBatch(lines); }
        catch (BusinessException ex) when (ex.Code == expected) { return; }
        throw new Exception("Expected CASH03 batch rejection: " + expected);
    }

    private static void Reject(string expected, decimal remaining, decimal allocated, decimal discount)
    {
        try { CashReceiptAllocation.Validate(remaining, allocated, discount); }
        catch (BusinessException ex) when (ex.Code == expected) { return; }
        throw new Exception("Expected CASH03 rejection: " + expected);
    }
}
