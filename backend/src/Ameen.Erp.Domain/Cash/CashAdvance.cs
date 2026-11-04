namespace Ameen.Erp.Domain;

// CASH06-01: structural foundation only. The HR03 holder reference, advance
// type values, settlement/reimbursement policies and GL postings are not approved.
// Every EF write remains blocked until the operational contract is specified.
public sealed class CashAdvance : CompanyEntity
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
    public Guid HolderId { get; private set; }
    public string AdvanceType { get; private set; } = "";
    public decimal Amount { get; private set; }
    public DateOnly ExpectedSettlementDate { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CashAdvance() { }

    public CashAdvance(Guid companyId, Guid branchId, Guid fiscalYearId,
        string number, DateOnly documentDate, DateOnly accountingDate,
        string currencyCode, decimal exchangeRate, string description,
        string? externalReference, string state, Guid holderId,
        string advanceType, decimal amount, DateOnly expectedSettlementDate,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty &&
            holderId != Guid.Empty, "cash_advance_reference_required",
            "الفرع والسنة وصاحب العهدة مراجع إلزامية.");
        Check.That(documentDate != default && accountingDate != default &&
            expectedSettlementDate != default, "cash_advance_date_required",
            "تواريخ المستند والأثر والتسوية المتوقعة مطلوبة.");
        Check.That(exchangeRate > 0 && exchangeRate < 10_000_000_000m &&
            decimal.Round(exchangeRate, 10) == exchangeRate,
            "invalid_exchange_rate", "سعر الصرف موجب ودقيق حتى 10 منازل.");
        Check.Amount(amount, positive: true);
        Check.That(createdAt != default, "cash_advance_created_at_required",
            "وقت إنشاء سند العهدة مطلوب.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        Number = Check.Text(number, "رقم مستند العهدة", 64);
        DocumentDate = documentDate;
        AccountingDate = accountingDate;
        CurrencyCode = Check.Currency(currencyCode);
        ExchangeRate = exchangeRate;
        Description = Check.Text(description, "بيان العهدة", 1000);
        ExternalReference = string.IsNullOrWhiteSpace(externalReference)
            ? null : Check.Text(externalReference, "المرجع الخارجي", 100);
        State = Check.Text(state, "حالة العهدة", 32);
        HolderId = holderId;
        AdvanceType = Check.Text(advanceType, "نوع العهدة", 32);
        Amount = amount;
        ExpectedSettlementDate = expectedSettlementDate;
        CreatedBy = Check.Text(createdBy, "منشئ العهدة", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}

// Conditional CASH06 settlement line fields. The expense/HR03 reference
// target is unspecified; storing an identifier does NOT authorize settlement.
public sealed class CashAdvanceLine : CompanyEntity
{
    public Guid AdvanceId { get; private set; }
    public Guid? ExpenseReferenceId { get; private set; }
    public decimal? SettlementAmount { get; private set; }
    public string? AttachmentReference { get; private set; }

    private CashAdvanceLine() { }

    public CashAdvanceLine(Guid companyId, Guid advanceId,
        Guid? expenseReferenceId, decimal? settlementAmount,
        string? attachmentReference) : base(companyId)
    {
        Check.That(advanceId != Guid.Empty && expenseReferenceId != Guid.Empty,
            "cash_advance_line_reference_invalid",
            "مرجع العهدة أو المصروف إن وجد غير صالح.");
        if (settlementAmount.HasValue)
            Check.Amount(settlementAmount.Value);
        AdvanceId = advanceId;
        ExpenseReferenceId = expenseReferenceId;
        SettlementAmount = settlementAmount;
        AttachmentReference = string.IsNullOrWhiteSpace(attachmentReference)
            ? null : Check.Text(attachmentReference, "مرفق التسوية", 300);
    }
}
