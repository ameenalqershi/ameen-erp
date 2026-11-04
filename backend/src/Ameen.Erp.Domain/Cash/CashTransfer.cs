namespace Ameen.Erp.Domain;

// CASH05 draft header structure. Persistence, account eligibility, balance
// reservations, approval, FX differences, fees and GL posting remain disabled.
public sealed class CashTransfer : CompanyEntity
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
    public CashTransferAccountKind SourceKind { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public CashTransferAccountKind DestinationKind { get; private set; }
    public Guid DestinationAccountId { get; private set; }
    public decimal SourceAmount { get; private set; }
    public decimal DestinationAmount { get; private set; }
    public string DestinationCurrencyCode { get; private set; } = "";
    public decimal DestinationExchangeRate { get; private set; }
    public decimal? Fee { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CashTransfer() { }

    public CashTransfer(Guid companyId, Guid branchId, Guid fiscalYearId,
        string number, DateOnly documentDate, DateOnly accountingDate,
        string currencyCode, decimal exchangeRate, string description,
        string? externalReference, string state,
        CashTransferAccountRef source, CashTransferAccountRef destination,
        decimal sourceAmount, decimal destinationAmount, string destinationCurrencyCode,
        decimal destinationExchangeRate, decimal? fee,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && fiscalYearId != Guid.Empty,
            "cash_transfer_reference_required", "الفرع والسنة مطلوبان.");
        Check.That(documentDate != default && accountingDate != default,
            "cash_transfer_date_required", "تاريخ الوثيقة والأثر مطلوبان.");
        Check.That(createdAt != default, "cash_transfer_created_at_required",
            "وقت إنشاء التحويل مطلوب.");
        CashTransferValidation.Validate(source, destination, currencyCode,
            destinationCurrencyCode, sourceAmount, destinationAmount,
            exchangeRate, destinationExchangeRate, fee);
        BranchId = branchId;
        FiscalYearId = fiscalYearId;
        Number = Check.Text(number, "رقم سند التحويل", 64);
        DocumentDate = documentDate;
        AccountingDate = accountingDate;
        CurrencyCode = Check.Currency(currencyCode);
        ExchangeRate = exchangeRate;
        Description = Check.Text(description, "بيان سند التحويل", 1000);
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null :
            Check.Text(externalReference, "المرجع الخارجي", 100);
        State = Check.Text(state, "حالة التحويل", 32);
        SourceKind = source.Kind;
        SourceAccountId = source.Id;
        DestinationKind = destination.Kind;
        DestinationAccountId = destination.Id;
        SourceAmount = sourceAmount;
        DestinationAmount = destinationAmount;
        DestinationCurrencyCode = Check.Currency(destinationCurrencyCode);
        DestinationExchangeRate = destinationExchangeRate;
        Fee = fee;
        CreatedBy = Check.Text(createdBy, "منشئ التحويل");
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
