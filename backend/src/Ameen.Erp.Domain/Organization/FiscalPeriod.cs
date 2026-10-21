namespace Ameen.Erp.Domain;

public sealed class FiscalPeriod : CompanyEntity
{
    public Guid FiscalYearId { get; private set; }
    public string Code { get; private set; } = "";
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public bool IsClosed { get; private set; }
    private FiscalPeriod() { }
    public FiscalPeriod(Guid companyId, Guid yearId, string code, DateOnly start, DateOnly end) : base(companyId)
    {
        Check.That(end >= start, "invalid_period", "نهاية الفترة قبل بدايتها.");
        FiscalYearId = yearId; Code = Check.Code(code, "رمز الفترة"); StartsOn = start; EndsOn = end;
    }
    public void SetClosed(bool closed)
    {
        Check.That(IsClosed != closed, "period_state_conflict", "الفترة في الحالة المطلوبة بالفعل.", ErrorKind.Conflict);
        IsClosed = closed; Touch();
    }
}
