namespace Ameen.Erp.Domain;

public sealed class RecurringJournalLine : CompanyEntity
{
    public Guid TemplateId { get; private set; }
    public int Position { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string Description { get; private set; } = "";
    public string CurrencyCode { get; private set; } = "";
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    private RecurringJournalLine() { }
    public RecurringJournalLine(Guid companyId, Guid templateId, int position, Guid accountId, Guid? centerId,
        string description, string currency, decimal debit, decimal credit, int currencyScale) : base(companyId)
    {
        Check.Amount(debit, currencyScale); Check.Amount(credit, currencyScale);
        Check.That((debit > 0 && credit == 0) || (credit > 0 && debit == 0), "invalid_journal_side", "السطر يحمل مبلغًا موجبًا في طرف واحد.");
        TemplateId = templateId; Position = position; AccountId = accountId; CostCenterId = centerId;
        Description = Check.Text(description, "بيان السطر", 500); CurrencyCode = Check.Currency(currency);
        Debit = debit; Credit = credit;
    }
}
