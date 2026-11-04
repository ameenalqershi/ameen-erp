namespace Ameen.Erp.Domain;

// CASH03 schema foundation only. Source invoices, payer eligibility, allowed
// payment methods, cheque settlement and posting belong to separate validated
// workflows. The EF write gate prevents this type from becoming a live receipt.
public sealed class CashReceipt : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    public string Number { get; private set; } = "";
    public DateOnly DocumentDate { get; private set; }
    public DateOnly AccountingDate { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public decimal ExchangeRate { get; private set; }
    public string Description { get; private set; } = "";
    public string? ExternalReference { get; private set; }
    public string State { get; private set; } = "";
    public Guid PayerId { get; private set; }
    public string PaymentMethod { get; private set; } = "";
    // CASH03-F15 is conditional and polymorphic (cashbox OR bank account).
    // The approved method-to-target policy has not been implemented.
    public Guid? CashBoxId { get; private set; }
    public Guid? BankAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? ChequeReference { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CashReceipt() { }

    public CashReceipt(Guid companyId, Guid branchId, Guid fiscalYearId, string number,
        DateOnly documentDate, DateOnly accountingDate, string currencyCode,
        decimal exchangeRate, string description, string? externalReference,
        string state, Guid payerId, string paymentMethod, Guid? cashBoxId,
        Guid? bankAccountId, decimal amount, string? chequeReference,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty &&
            payerId != Guid.Empty, "cash_receipt_reference_required",
            "الفرع والسنة وجهة القبض مطلوبة.");
        Check.That(cashBoxId != Guid.Empty && bankAccountId != Guid.Empty,
            "cash_receipt_destination_invalid", "مرجع الصندوق أو البنك إن وجد يجب أن يكون صحيحًا.");
        Check.That(documentDate != default && accountingDate != default,
            "cash_receipt_date_required", "تاريخ المستند وتاريخ الأثر مطلوبان.");
        Check.That(exchangeRate > 0 && exchangeRate < 10_000_000_000m &&
            decimal.Round(exchangeRate, 10) == exchangeRate, "invalid_exchange_rate",
            "سعر الصرف موجب وبدقة لا تزيد عن عشرة منازل.");
        Check.Amount(amount, positive: true);
        Check.That(createdAt != default, "cash_receipt_created_at_required",
            "وقت إنشاء سند القبض مطلوب.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        // Preserve leading zeroes; document numbering scope is deferred to SYS10.
        Number = Check.Text(number, "رقم سند القبض", 64);
        DocumentDate = documentDate;
        AccountingDate = accountingDate;
        CurrencyCode = Check.Currency(currencyCode);
        ExchangeRate = exchangeRate;
        Description = Check.Text(description, "بيان سند القبض", 1000);
        ExternalReference = OptionalText(externalReference, "مرجع خارجي", 100);
        State = Check.Text(state, "حالة سند القبض", 32);
        PayerId = payerId;
        PaymentMethod = Check.Text(paymentMethod, "طريقة القبض", 32);
        CashBoxId = cashBoxId;
        BankAccountId = bankAccountId;
        Amount = amount;
        ChequeReference = OptionalText(chequeReference, "مرجع الشيك", 100);
        CreatedBy = Check.Text(createdBy, "منشئ سند القبض");
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }

    private static string? OptionalText(string? value, string field, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : Check.Text(value, field, length);
}

// A saved financial allocation requires SAL11 current remaining balances and
// an atomic posting guard, which is NOT part of this foundation.
public sealed class CashReceiptLine : CompanyEntity
{
    public Guid ReceiptId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public decimal SettlementDiscount { get; private set; }

    private CashReceiptLine() { }

    public CashReceiptLine(Guid companyId, Guid receiptId, Guid? invoiceId,
        decimal allocatedAmount, decimal settlementDiscount) : base(companyId)
    {
        Check.That(receiptId != Guid.Empty && invoiceId != Guid.Empty,
            "cash_receipt_line_reference_invalid", "السند ومرجع الفاتورة إن وجد يجب أن يكونا صالحين.");
        Check.Amount(allocatedAmount);
        Check.Amount(settlementDiscount);
        ReceiptId = receiptId;
        InvoiceId = invoiceId;
        AllocatedAmount = allocatedAmount;
        SettlementDiscount = settlementDiscount;
    }
}
