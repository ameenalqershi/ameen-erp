namespace Ameen.Erp.Domain;

// SYS12-01 schema-only card. The original source does not define the code
// catalogues or template markup/legally mandatory country fields. Do not
// permit use, edits, or printing until SYS12-02 is separately accepted.
public sealed class PrintTemplate : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public string DocumentTypeCode { get; private set; } = "";
    public string LanguageCode { get; private set; } = "";
    public string PaperSizeCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string VersionCode { get; private set; } = "";
    public bool IsDraft { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }

    private PrintTemplate() { }

    public PrintTemplate(Guid companyId, Guid? branchId, string statusCode,
        string documentTypeCode, string languageCode, string paperSizeCode,
        string name, string versionCode, bool isDraft, string createdBy,
        DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "print_template_branch_invalid", "الفرع غير صالح.");
        BranchId = branchId;
        StatusCode = Check.Text(statusCode, "حالة القالب", 32);
        DocumentTypeCode = Check.Text(documentTypeCode, "نوع الوثيقة", 64);
        LanguageCode = Check.Text(languageCode, "اللغة", 32);
        PaperSizeCode = Check.Text(paperSizeCode, "مقاس الورق", 32);
        Name = Check.Text(name, "اسم القالب", 200);
        VersionCode = Check.Code(versionCode, "نسخة القالب");
        IsDraft = isDraft;
        CreatedBy = Check.Text(createdBy, "منشئ القالب", 200);
        CreatedAt = now;
        LastModifiedAt = now;
    }
}
