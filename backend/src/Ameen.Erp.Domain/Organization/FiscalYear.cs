namespace Ameen.Erp.Domain;

public sealed class FiscalYear : CompanyEntity
{
    public string Code { get; private set; } = "";
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    // Legacy years predate per-card audit metadata. Null preserves unknown
    // provenance; new years always record real actor/time and revision one.
    public string? CreatedBy { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public int? Revision { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }
    private FiscalYear() { }
    public FiscalYear(Guid companyId, string code, DateOnly start, DateOnly end) : base(companyId)
    {
        Check.That(start != default && end != default, "fiscal_year_date_required",
            "بداية السنة المالية ونهايتها مطلوبتان.");
        Check.That(end >= start && end.DayNumber - start.DayNumber < 367, "invalid_fiscal_year", "مدة السنة المالية من يوم إلى 367 يومًا.");
        Code = Check.Code(code, "رمز السنة", 16); StartsOn = start; EndsOn = end;
    }
    public FiscalYear(Guid companyId, string code, DateOnly start, DateOnly end,
        string createdBy, DateTimeOffset now) : this(companyId, code, start, end)
    {
        CreatedBy = Check.Text(createdBy, "منشئ السنة", 200);
        Check.That(now != default, "invalid_fiscal_audit_time", "وقت إنشاء السنة غير صالح.");
        CreatedAt = LastModifiedAt = now;
        Revision = 1;
    }
}
