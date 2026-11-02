using System.Globalization;
namespace Ameen.Erp.Domain;
public sealed class DocumentNumberSeries : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public NumberingDocumentKind DocumentKind { get; private set; }
    public Guid FiscalYearId { get; private set; }
    public string? Prefix { get; private set; }
    public long NextNumber { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    private DocumentNumberSeries() { }
    public DocumentNumberSeries(Guid cid, Guid? branchId, NumberingDocumentKind kind, Guid yearId,
        string? prefix, long next, string actor, DateTimeOffset now) : base(cid)
    {
        Check.That(Enum.IsDefined(kind), "invalid_numbering_kind", "نوع مستند غير صالح.");
        Check.That(branchId != Guid.Empty && yearId != Guid.Empty, "invalid_numbering_scope", "نطاق الترقيم غير صالح.");
        BranchId=branchId; DocumentKind=kind; FiscalYearId=yearId;
        CreatedBy=Check.Text(actor,"المنشئ"); CreatedAt=now; LastModifiedAt=now;
        Prefix=ValidatedPrefix(prefix,next); NextNumber=next;
    }
    // Validate before changing any aggregate fields: a failed edit must not
    // leave a new prefix/counter behind when the audit revision is invalid.
    private static string? ValidatedPrefix(string? prefix,long next)
    {
        Check.That(next>0 && next<long.MaxValue,"invalid_next_number","الرقم التالي موجب ودون الحد الأقصى.");
        var validated=string.IsNullOrWhiteSpace(prefix)?null:Check.Text(prefix,"البادئة",32);
        Check.That(validated is null || !validated.Any(char.IsControl) && !validated.Contains("..",StringComparison.Ordinal),"invalid_number_prefix","البادئة لا تتضمن محارف تحكم.");
        return validated;
    }
    private void Advance(DateTimeOffset now)
    {
        Check.That(Revision<int.MaxValue && now>=CreatedAt && now>=LastModifiedAt,"invalid_numbering_revision","إصدار أو وقت تعديل الترقيم غير صالح.");
        Revision++; LastModifiedAt=now; Touch();
    }
    public void Edit(string? prefix,long next,DateTimeOffset now)
    {
        Check.That(next>=NextNumber,"number_reuse_forbidden","لا يجوز خفض الرقم التالي وإعادة استخدام أرقام محجوزة.",ErrorKind.Conflict);
        var validatedPrefix=ValidatedPrefix(prefix,next);
        Advance(now);
        Prefix=validatedPrefix; NextNumber=next;
    }
    public void SetActive(bool active,DateTimeOffset now) { Advance(now); IsActive=active; }
    public string Candidate() => (Prefix??"")+NextNumber.ToString("D6",CultureInfo.InvariantCulture);
    public (long Sequence,string Number) Allocate(DateTimeOffset now)
    {
        Check.That(IsActive,"numbering_inactive","سلسلة الترقيم معطلة.",ErrorKind.Conflict);
        Check.That(NextNumber<long.MaxValue-1,"numbering_exhausted","نفدت سلسلة الأرقام.",ErrorKind.Conflict);
        var result=(NextNumber,Candidate());
        Advance(now);
        NextNumber++;
        return result;
    }
}
