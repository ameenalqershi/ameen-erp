namespace Ameen.Erp.Domain;

// CASH03-CTRL01: allocation guard only. Does not post, reserve a balance,
// recognize a pending cheque, or choose an unapproved collection method.
public static class CashReceiptAllocation
{
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
