namespace Ameen.Erp.Domain;

// SYS11-01: schema-only foundation. The original analysis does not enumerate
// valuation, delivery, event, account-role or status codes. These are opaque
// reference values until a reviewed catalogue and authorization are supplied.
// EF forbids writes, including direct persistence, until SYS11-02 is approved.
public sealed class GeneralOptionsPolicy : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public string InventoryValuationCode { get; private set; } = "";
    public bool BlockNegativeStock { get; private set; }
    public string SalesDeliveryModeCode { get; private set; } = "";
    public int QuantityPrecision { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<GeneralOptionsAccountLine> Lines { get; private set; } = new();

    private GeneralOptionsPolicy() { }
    // Construction checks structure only and is deliberately not an
    // authorization or an approval of the supplied opaque catalogue codes.
    public GeneralOptionsPolicy(Guid companyId, Guid? branchId, string statusCode,
        string valuationCode, bool blockNegativeStock, string deliveryModeCode,
        int quantityPrecision, string createdBy, DateTimeOffset now,
        IReadOnlyList<(string EventCode, string AccountRoleCode, Guid AccountId, string Scope)> lines)
        : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "general_options_branch_invalid", "الفرع غير صالح.");
        Check.That(quantityPrecision >= 0, "general_options_precision_invalid", "دقة الكمية غير صالحة.");
        BranchId = branchId;
        StatusCode = Check.Text(statusCode, "حالة الخيارات", 32);
        InventoryValuationCode = Check.Text(valuationCode, "نوع تقييم المخزون", 64);
        SalesDeliveryModeCode = Check.Text(deliveryModeCode, "نمط البيع والتسليم", 64);
        BlockNegativeStock = blockNegativeStock;
        QuantityPrecision = quantityPrecision;
        CreatedBy = Check.Text(createdBy, "المنشئ", 200);
        Check.That(now != default, "general_options_audit_time_invalid", "وقت إنشاء السياسة غير صالح.");
        CreatedAt = now;
        LastModifiedAt = now;
        Check.That(lines is not null, "general_options_lines_invalid", "سطور الربط غير صالحة.");
        Lines = lines!.Select(x => new GeneralOptionsAccountLine(
            companyId, Id, x.EventCode, x.AccountRoleCode, x.AccountId, x.Scope)).ToList();
    }
}

public sealed class GeneralOptionsAccountLine : CompanyEntity
{
    public Guid PolicyId { get; private set; }
    public string EventCode { get; private set; } = "";
    public string AccountRoleCode { get; private set; } = "";
    public Guid AccountId { get; private set; }
    public string Scope { get; private set; } = "";

    private GeneralOptionsAccountLine() { }
    public GeneralOptionsAccountLine(Guid companyId, Guid policyId, string eventCode,
        string accountRoleCode, Guid accountId, string scope) : base(companyId)
    {
        Check.That(policyId != Guid.Empty && accountId != Guid.Empty,
            "general_options_account_invalid", "مرجع الربط أو الحساب غير صالح.");
        PolicyId = policyId;
        EventCode = Check.Text(eventCode, "الحدث", 64);
        AccountRoleCode = Check.Text(accountRoleCode, "دور الحساب", 64);
        Scope = Check.Text(scope, "النطاق", 200);
        AccountId = accountId;
    }
}
