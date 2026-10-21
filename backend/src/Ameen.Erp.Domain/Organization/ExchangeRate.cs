namespace Ameen.Erp.Domain;

public sealed class ExchangeRate : CompanyEntity
{
    public string CurrencyCode { get; private set; } = "";
    public DateOnly EffectiveDate { get; private set; }
    public decimal RateToFunctional { get; private set; }
    public string Source { get; private set; } = "";
    private ExchangeRate() { }
    public ExchangeRate(Guid companyId, string currency, DateOnly date, decimal rate, string source) : base(companyId)
    {
        Check.That(rate > 0 && rate < 1_000_000_000m && decimal.Round(rate, 10) == rate,
            "invalid_exchange_rate", "سعر الصرف موجب وبحد أقصى عشر خانات عشرية.");
        CurrencyCode = Check.Currency(currency); EffectiveDate = date;
        RateToFunctional = rate; Source = Check.Text(source, "مصدر السعر");
    }
}
