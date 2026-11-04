namespace Ameen.Erp.Domain;

// SYS15-01: schema-only definition. No backup worker, production restore,
// scheduler, destination access or encryption pipeline is activated here.
public sealed class BackupJobConfiguration : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string ScheduleText { get; private set; } = "";
    public string Destination { get; private set; } = "";
    // The original field is a whole-number retention policy with no stated unit.
    public int RetentionPolicy { get; private set; }
    public bool IsEncrypted { get; private set; }
    public string? LastTestReference { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }

    private BackupJobConfiguration() { }
    public BackupJobConfiguration(Guid companyId, Guid? branchId, string statusCode,
        string name, string scheduleText, string destination, int retentionPolicy,
        bool isEncrypted, string? lastTestReference, string createdBy,
        DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "backup_branch_invalid", "مرجع الفرع غير صالح.");
        Check.That(retentionPolicy >= 0, "backup_retention_invalid", "سياسة الاحتفاظ لا تكون سالبة.");
        BranchId = branchId;
        StatusCode = Check.Text(statusCode, "حالة المهمة", 32);
        Name = Check.Text(name, "اسم مهمة النسخ", 200);
        ScheduleText = Check.Text(scheduleText, "الجدول الزمني", 200);
        Destination = Check.Text(destination, "وجهة النسخ", 2000);
        RetentionPolicy = retentionPolicy;
        IsEncrypted = isEncrypted;
        LastTestReference = string.IsNullOrWhiteSpace(lastTestReference) ? null :
            Check.Text(lastTestReference, "مرجع آخر اختبار", 200);
        CreatedBy = Check.Text(createdBy, "المنشئ", 200);
        Check.That(now != default, "backup_audit_time_invalid", "وقت إنشاء مهمة النسخ غير صالح.");
        CreatedAt = now;
        LastModifiedAt = now;
    }
}
