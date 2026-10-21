namespace Ameen.Erp.Domain;

public sealed class FiscalYear : CompanyEntity
{
    public string Code { get; private set; } = "";
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    private FiscalYear() { }
    public FiscalYear(Guid companyId, string code, DateOnly start, DateOnly end) : base(companyId)
    {
        Check.That(end >= start && end.DayNumber - start.DayNumber < 367, "invalid_fiscal_year", "مدة السنة المالية من يوم إلى 367 يومًا.");
        Code = Check.Code(code, "رمز السنة", 16); StartsOn = start; EndsOn = end;
    }
}
