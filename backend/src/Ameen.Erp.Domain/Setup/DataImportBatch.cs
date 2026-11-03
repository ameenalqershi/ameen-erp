namespace Ameen.Erp.Domain;

// SYS13-01: metadata/schema only. Import processing, validation, replay,
// preview, account postings and rejection are NOT enabled by this type.
public sealed class DataImportBatch : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public string DataKindCode { get; private set; } = "";
    public string FileIdentifier { get; private set; } = "";
    public DateOnly CutoffDate { get; private set; }
    public string DuplicatePolicyCode { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<DataImportFieldMapping> Mappings { get; private set; } = new();

    private DataImportBatch() { }
    public DataImportBatch(Guid companyId, Guid? branchId, string statusCode,
        string dataKindCode, string fileIdentifier, DateOnly cutoffDate,
        string duplicatePolicyCode, string createdBy, DateTimeOffset now,
        IReadOnlyList<(string SourceColumn, string TargetField, string? DefaultValue)> mappings)
        : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "data_import_branch_invalid", "مرجع الفرع غير صالح.");
        Check.That(cutoffDate != DateOnly.MinValue, "data_import_cutoff_required", "تاريخ القطع مطلوب.");
        Check.That(mappings is { Count: > 0 }, "data_import_mappings_required",
            "الربط بين أعمدة الملف والحقول مطلوب.");
        BranchId = branchId;
        StatusCode = Check.Text(statusCode, "الحالة", 32);
        DataKindCode = Check.Text(dataKindCode, "نوع البيانات", 64);
        FileIdentifier = Check.Text(fileIdentifier, "معرف الملف", 200);
        CutoffDate = cutoffDate;
        DuplicatePolicyCode = Check.Text(duplicatePolicyCode, "سياسة التكرار", 64);
        CreatedBy = Check.Text(createdBy, "المنشئ", 200);
        CreatedAt = now; LastModifiedAt = now;
        Mappings = mappings.Select(x => new DataImportFieldMapping(
            companyId, Id, x.SourceColumn, x.TargetField, x.DefaultValue)).ToList();
    }
}

public sealed class DataImportFieldMapping : CompanyEntity
{
    public Guid BatchId { get; private set; }
    public string SourceColumn { get; private set; } = "";
    public string TargetField { get; private set; } = "";
    public string? DefaultValue { get; private set; }

    private DataImportFieldMapping() { }
    public DataImportFieldMapping(Guid companyId, Guid batchId, string sourceColumn,
        string targetField, string? defaultValue) : base(companyId)
    {
        Check.That(batchId != Guid.Empty, "data_import_batch_required", "سجل الاستيراد مطلوب.");
        BatchId = batchId;
        SourceColumn = Check.Text(sourceColumn, "عمود المصدر", 200);
        TargetField = Check.Text(targetField, "حقل الهدف", 200);
        DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null :
            Check.Text(defaultValue, "القيمة الافتراضية", 2000);
    }
}
