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

        test("CASH03-AC03: a discount may exactly settle the remaining invoice", f =>
        {
            CashReceiptAllocation.Validate(100m, 0.000001m, 99.999999m);
            return Task.CompletedTask;
        });

        test("CASH03-AC04: allocation precision cannot be rounded to pass", f =>
        {
            Reject("invalid_amount", 1m, 0.0000001m, 0m);
            Reject("invalid_amount", 1m, 0.000001m, 0.0000001m);
            return Task.CompletedTask;
        });

        test("CASH03-AC05: distinct invoices retain independent remaining limits", f =>
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            CashReceiptAllocation.ValidateBatch(new[] {
                new CashReceiptAllocationLine(first, 10m, 9m, 1m),
                new CashReceiptAllocationLine(second, 10m, 9m, 1m)
            });
            return Task.CompletedTask;
        });

        test("CASH03-AC06: a later invalid allocation rejects the whole batch", f =>
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            RejectBatch("cash_receipt_allocation_exceeds_remaining", new[] {
                new CashReceiptAllocationLine(first, 100m, 50m, 0m),
                new CashReceiptAllocationLine(second, 1m, 1m, 0.000001m)
            });
            return Task.CompletedTask;
        });

        test("CASH03-AC02: many valid invoice lines cannot overflow the allocation guard", f =>
        {
            var invoice = Guid.NewGuid();
            var otherInvoice = Guid.NewGuid();
            var upper = 99_999_999_999_999m;
            RejectBatch("cash_receipt_allocation_exceeds_remaining", new[] {
                new CashReceiptAllocationLine(invoice, upper, upper, 0m),
                new CashReceiptAllocationLine(invoice, upper, upper, 0m)
            });
            // Different invoices are independent, even at the amount ceiling.
            CashReceiptAllocation.ValidateBatch(new[] {
                new CashReceiptAllocationLine(invoice, upper, upper, 0m),
                new CashReceiptAllocationLine(otherInvoice, upper, upper, 0m)
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
