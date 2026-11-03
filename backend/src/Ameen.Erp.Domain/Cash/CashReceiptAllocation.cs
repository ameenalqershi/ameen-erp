namespace Ameen.Erp.Domain;

// CASH03-CTRL01: allocation guard only. Does not post, reserve a balance,
// recognize a pending cheque, or choose an unapproved collection method.
public static class CashReceiptAllocation
{
    // Group repeated invoice references before comparing to the open amount.
    // A per-line check alone would allow two individually valid lines to over-allocate.
    public static void ValidateBatch(IEnumerable<CashReceiptAllocationLine> lines)
    {
        var list = lines.ToList();
        foreach (var line in list)
        {
            Check.That(line.InvoiceId != Guid.Empty, "cash_receipt_invoice_required",
                "مرجع الفاتورة المفتوحة مطلوب.");
            Validate(line.Remaining, line.Allocated, line.SettlementDiscount);
        }
        foreach (var group in list.GroupBy(x => x.InvoiceId))
        {
            var remaining = group.First().Remaining;
            Check.That(group.All(x => x.Remaining == remaining),
                "cash_receipt_remaining_mismatch", "المتبقي من الفاتورة غير متطابق بين السطور.");
            // Compare against the remaining capacity instead of summing: even
            // individually valid amounts can overflow decimal when repeated.
            var available = remaining;
            foreach (var line in group)
            {
                var applied = line.Allocated + line.SettlementDiscount;
                Check.That(applied <= available,
                    "cash_receipt_allocation_exceeds_remaining",
                    "مجموع التخصيصات وخصومات التسوية يتجاوز المتبقي من الفاتورة.");
                available -= applied;
            }
        }
    }

    public static void Validate(decimal remaining, decimal allocated, decimal settlementDiscount)
    {
        Check.Amount(remaining);
        Check.Amount(allocated, positive: true);
        Check.Amount(settlementDiscount);
        // The amount applied to an open invoice includes the settlement discount.
        // Compare without rounding; all operands already passed precision validation.
        Check.That(allocated + settlementDiscount <= remaining,
            "cash_receipt_allocation_exceeds_remaining",
            "مجموع التخصيص وخصم التسوية يتجاوز المتبقي من الفاتورة.");
    }
}

public sealed record CashReceiptAllocationLine(Guid InvoiceId, decimal Remaining,
    decimal Allocated, decimal SettlementDiscount);
