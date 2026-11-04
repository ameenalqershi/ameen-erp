namespace Ameen.Erp.Domain;

// CASH07/CASH08 are schema-only models. No clearing, rejection, cancellation,
// bank recognition or source document posting is enabled.
public sealed class IncomingCheque : CompanyEntity
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
    public string ChequeNumber { get; private set; } = "";
    public string DrawnBankName { get; private set; } = "";
    public Guid PayerId { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public Guid CollectionAccountId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private IncomingCheque() { }

    public IncomingCheque(Guid companyId, Guid branchId, Guid fiscalYearId,
        string number, DateOnly documentDate, DateOnly accountingDate,
        string currencyCode, decimal exchangeRate, string description,
        string? externalReference, string state, string chequeNumber,
        string drawnBankName, Guid payerId, decimal amount, DateOnly dueDate,
        Guid collectionAccountId, string createdBy, DateTimeOffset createdAt)
        : base(companyId)
    {
        ChequeFoundationRules.ValidateCommon(branchId, fiscalYearId,
            documentDate, accountingDate, exchangeRate, amount, dueDate);
        Check.That(payerId != Guid.Empty && collectionAccountId != Guid.Empty,
            "cheque_reference_required", "الدافع وحساب تحت التحصيل مطلوبان.");
        Check.That(createdAt != default, "cheque_created_at_required",
            "وقت إنشاء الشيك مطلوب.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        Number = Check.Text(number, "رقم المستند", 64);
        DocumentDate = documentDate;
        AccountingDate = accountingDate;
        CurrencyCode = Check.Currency(currencyCode);
        ExchangeRate = exchangeRate;
        Description = Check.Text(description, "البيان", 1000);
        ExternalReference = ChequeFoundationRules.Optional(externalReference, 100);
        State = Check.Text(state, "الحالة", 32);
        ChequeNumber = Check.Text(chequeNumber, "رقم الشيك", 64);
        DrawnBankName = Check.Text(drawnBankName, "البنك المسحوب عليه", 200);
        PayerId = payerId;
        Amount = amount;
        DueDate = dueDate;
        CollectionAccountId = collectionAccountId;
        CreatedBy = Check.Text(createdBy, "منشئ الشيك", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}

public sealed class OutgoingCheque : CompanyEntity
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
    public string ChequeNumber { get; private set; } = "";
    public Guid BankAccountId { get; private set; }
    public Guid BeneficiaryId { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string ChequeState { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private OutgoingCheque() { }

    public OutgoingCheque(Guid companyId, Guid branchId, Guid fiscalYearId,
        string number, DateOnly documentDate, DateOnly accountingDate,
        string currencyCode, decimal exchangeRate, string description,
        string? externalReference, string state, string chequeNumber,
        Guid bankAccountId, Guid beneficiaryId, decimal amount, DateOnly dueDate,
        string chequeState, string createdBy, DateTimeOffset createdAt)
        : base(companyId)
    {
        ChequeFoundationRules.ValidateCommon(branchId, fiscalYearId,
            documentDate, accountingDate, exchangeRate, amount, dueDate);
        Check.That(bankAccountId != Guid.Empty && beneficiaryId != Guid.Empty,
            "cheque_reference_required", "البنك والمستفيد مطلوبان.");
        Check.That(createdAt != default, "cheque_created_at_required",
            "وقت إنشاء الشيك مطلوب.");
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        Number = Check.Text(number, "رقم المستند", 64);
        DocumentDate = documentDate;
        AccountingDate = accountingDate;
        CurrencyCode = Check.Currency(currencyCode);
        ExchangeRate = exchangeRate;
        Description = Check.Text(description, "البيان", 1000);
        ExternalReference = ChequeFoundationRules.Optional(externalReference, 100);
        State = Check.Text(state, "الحالة", 32);
        ChequeNumber = Check.Text(chequeNumber, "رقم الشيك", 64);
        BankAccountId = bankAccountId;
        BeneficiaryId = beneficiaryId;
        Amount = amount;
        DueDate = dueDate;
        // Stored only, not a guessed transition or permission policy.
        ChequeState = Check.Text(chequeState, "حالة الشيك", 32);
        CreatedBy = Check.Text(createdBy, "منشئ الشيك", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}

internal static class ChequeFoundationRules
{
    internal static void ValidateCommon(Guid branchId, Guid fiscalYearId,
        DateOnly documentDate, DateOnly accountingDate, decimal exchangeRate,
        decimal amount, DateOnly dueDate)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty,
            "cheque_reference_required", "الفرع والسنة مطلوبان.");
        Check.That(documentDate != default && accountingDate != default && dueDate != default,
            "cheque_date_required", "تواريخ الوثيقة والأثر والاستحقاق مطلوبة.");
        Check.That(exchangeRate > 0 && exchangeRate < 10_000_000_000m &&
            decimal.Round(exchangeRate, 10) == exchangeRate,
            "invalid_exchange_rate", "سعر الصرف غير صالح أو يتجاوز دقة الحقل.");
        Check.Amount(amount, positive: true);
    }

    internal static string? Optional(string? value, int limit) =>
        string.IsNullOrWhiteSpace(value) ? null : Check.Text(value, "مرجع خارجي", limit);
}
