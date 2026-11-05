namespace Ameen.Erp.Domain;

// SAL09: field-complete domain foundation only. Operational scope and
// commission/employee references are not approved for persistence or access.
public sealed class SalesRepresentative : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public string Region { get; private set; } = "";
    public Guid? SupervisorId { get; private set; }
    public Guid? CustodyAccountId { get; private set; }
    public Guid? CommissionPolicyId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private SalesRepresentative() { }

    public SalesRepresentative(Guid companyId, string code, string name, string region,
        string createdBy, DateTimeOffset createdAt, string? englishName = null,
        string? notes = null, Guid? employeeId = null, Guid? supervisorId = null,
        Guid? custodyAccountId = null, Guid? commissionPolicyId = null) : base(companyId)
    {
        Check.That(createdAt != default, "sales_rep_audit_date_required", "وقت الإنشاء مطلوب.");
        Check.That(employeeId != Guid.Empty && supervisorId != Guid.Empty &&
            custodyAccountId != Guid.Empty && commissionPolicyId != Guid.Empty,
            "sales_rep_invalid_reference", "المراجع الاختيارية لا تقبل المعرف الصفري.");
        Code = Check.Code(code, "رمز المندوب");
        Name = Check.Text(name, "اسم المندوب");
        Region = Check.Text(region, "منطقة المندوب");
        CreatedBy = Check.Text(createdBy, "منشئ المندوب", 200);
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "ملاحظات المندوب", 2000);
        EmployeeId = employeeId;
        SupervisorId = supervisorId;
        CustodyAccountId = custodyAccountId;
        CommissionPolicyId = commissionPolicyId;
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
