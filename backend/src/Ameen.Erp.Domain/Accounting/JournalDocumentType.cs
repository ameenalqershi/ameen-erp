namespace Ameen.Erp.Domain;

// GL14: a configuration card, not an authority to post or impersonate a source.
// Activation and use remain unavailable until SYS09 approval routes can be validated.
public enum JournalDocumentOrigin { Manual = 0, Automatic = 1 }

public sealed class JournalDocumentType : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public JournalDocumentOrigin Origin { get; private set; }
    public Guid NumberSeriesId { get; private set; }
    public Guid ApprovalRouteId { get; private set; }
    public bool RequiresAttachment { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }

    private JournalDocumentType() { }

    public JournalDocumentType(Guid companyId, Guid? branchId, string code, string name,
        JournalDocumentOrigin origin, Guid numberSeriesId, Guid approvalRouteId,
        bool requiresAttachment, string actor, DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "invalid_document_type_branch", "الفرع غير صالح.");
        Check.That(Enum.IsDefined(origin), "invalid_document_type_origin", "مصدر نوع المستند غير صالح.");
        Check.That(numberSeriesId != Guid.Empty && approvalRouteId != Guid.Empty,
            "invalid_document_type_reference", "التسلسل ومسار الاعتماد مطلوبان.");
        BranchId = branchId;
        Code = Check.Code(code, "رمز نوع المستند", 32);
        Name = Check.Text(name, "اسم نوع المستند", 200);
        Origin = origin;
        NumberSeriesId = numberSeriesId;
        ApprovalRouteId = approvalRouteId;
        RequiresAttachment = requiresAttachment;
        CreatedBy = Check.Text(actor, "منشئ نوع المستند", 200);
        CreatedAt = LastModifiedAt = now;
    }

    public void Edit(string name, Guid numberSeriesId, Guid approvalRouteId,
        bool requiresAttachment, DateTimeOffset now)
    {
        Check.That(numberSeriesId != Guid.Empty && approvalRouteId != Guid.Empty,
            "invalid_document_type_reference", "التسلسل ومسار الاعتماد مطلوبان.");
        var validatedName = Check.Text(name, "اسم نوع المستند", 200);
        Advance(now);
        Name = validatedName;
        NumberSeriesId = numberSeriesId;
        ApprovalRouteId = approvalRouteId;
        RequiresAttachment = requiresAttachment;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        Advance(now);
        IsActive = active;
    }

    private void Advance(DateTimeOffset now)
    {
        Check.That(Revision < int.MaxValue && now >= CreatedAt && now >= LastModifiedAt,
            "invalid_document_type_revision", "إصدار أو وقت التعديل غير صالح.", ErrorKind.Conflict);
        Revision++;
        LastModifiedAt = now;
        Touch();
    }
}
