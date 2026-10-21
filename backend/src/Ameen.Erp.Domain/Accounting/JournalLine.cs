namespace Ameen.Erp.Domain;

public sealed class JournalLine : CompanyEntity
{
    public Guid JournalId { get; private set; }
    public int Position { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string Description { get; private set; } = "";
    public string CurrencyCode { get; private set; } = "";
    public Guid? ExchangeRateId { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public decimal FunctionalDebit { get; private set; }
    public decimal FunctionalCredit { get; private set; }
    private JournalLine() { }
    public JournalLine(Guid companyId, Guid journalId, int position, Guid accountId, Guid? centerId,
        string description, string currency, Guid? rateId, decimal rate, decimal debit, decimal credit,
        int currencyScale, int functionalScale) : base(companyId)
    {
        Check.Amount(debit, currencyScale); Check.Amount(credit, currencyScale);
        Check.That((debit > 0 && credit == 0) || (credit > 0 && debit == 0),
            "invalid_journal_side", "السطر يحمل مبلغًا موجبًا في طرف واحد فقط.");
        Check.That(rate > 0 && rate < 1_000_000_000m && decimal.Round(rate, 10) == rate,
            "invalid_exchange_rate", "سعر الصرف غير صالح.");
        var fd = decimal.Round(debit * rate, functionalScale, MidpointRounding.AwayFromZero);
        var fc = decimal.Round(credit * rate, functionalScale, MidpointRounding.AwayFromZero);
        Check.Amount(fd); Check.Amount(fc);
        Check.That(fd + fc > 0, "zero_functional_amount", "قيمة السطر بعد التحويل والتقريب تساوي صفرًا.");
        JournalId = journalId; Position = position; AccountId = accountId; CostCenterId = centerId;
        Description = Check.Text(description, "بيان السطر", 500); CurrencyCode = Check.Currency(currency);
        ExchangeRateId = rateId; ExchangeRate = rate; Debit = debit; Credit = credit;
        FunctionalDebit = fd; FunctionalCredit = fc;
    }
    public JournalLine Opposite(Guid journalId)
    {
        // Reverse the exact historical amounts and snapshots, not today's rates.
        return new JournalLine
        {
            CompanyId = CompanyId, JournalId = journalId, Position = Position,
            AccountId = AccountId, CostCenterId = CostCenterId, Description = Description,
            CurrencyCode = CurrencyCode, ExchangeRateId = ExchangeRateId, ExchangeRate = ExchangeRate,
            Debit = Credit, Credit = Debit, FunctionalDebit = FunctionalCredit, FunctionalCredit = FunctionalDebit
        };
    }
}
