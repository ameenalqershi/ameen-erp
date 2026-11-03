namespace Ameen.Erp.Domain;

public sealed class Membership : CompanyEntity
{
    public string Subject { get; private set; } = "";
    public string[] GrantedPermissions { get; private set; } = Array.Empty<string>();
    public Guid[] BranchIds { get; private set; } = Array.Empty<Guid>();
    public bool AllBranches { get; private set; }
    public bool IsActive { get; private set; } = true;
    // Identity-profile provisioning remains owned by the IdP; these fields
    // cover ERP membership grants only. Legacy creation provenance is unknown.
    public string? CreatedBy { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public int? Revision { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }
    private Membership() { }
    public Membership(Guid companyId, string subject, IEnumerable<string> permissions,
        bool allBranches, IEnumerable<Guid> branches) : base(companyId)
    {
        Subject = Check.Text(subject, "معرف المستخدم لدى جهة الهوية");
        Configure(permissions, allBranches, branches);
    }
    public Membership(Guid companyId, string subject, IEnumerable<string> permissions,
        bool allBranches, IEnumerable<Guid> branches, string createdBy, DateTimeOffset now)
        : this(companyId, subject, permissions, allBranches, branches)
    {
        CreatedBy = Check.Text(createdBy, "منشئ العضوية", 200);
        Check.That(now != default, "invalid_membership_audit_time", "وقت إنشاء العضوية غير صالح.");
        CreatedAt = LastModifiedAt = now;
        Revision = 1;
    }
    // Called by the service after its permission/version checks and successful mutation.
    // Old memberships preserve unknown revision and creator rather than fabricated history.
    public void StampAuthorizedChange(DateTimeOffset now)
    {
        Check.That(now != default && (CreatedAt is null || now >= CreatedAt),
            "invalid_membership_audit_time", "وقت تعديل العضوية غير صالح.");
        if (Revision is int revision)
        {
            Check.That(revision < int.MaxValue, "membership_revision_overflow", "رقم إصدار العضوية استنفد.");
            Revision = revision + 1;
            LastModifiedAt = now;
        }
    }
    public void ConfigureAuthorized(IEnumerable<string> permissions, bool allBranches,
        IEnumerable<Guid> branches, DateTimeOffset now)
    {
        Configure(permissions, allBranches, branches);
        StampAuthorizedChange(now);
    }
    public void SetActiveAuthorized(bool active, DateTimeOffset now)
    {
        SetActive(active);
        StampAuthorizedChange(now);
    }
    public void Configure(IEnumerable<string> permissions, bool allBranches, IEnumerable<Guid> branches)
    {
        var grants = permissions.Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray();
        Check.That(grants.Length > 0 && grants.All(x => Permissions.All.Contains(x)), "invalid_permissions", "قائمة الصلاحيات غير صالحة.");
        var scope = branches.Distinct().ToArray();
        Check.That(allBranches || scope.Length > 0, "empty_branch_scope", "حدد الفروع المسموحة.");
        GrantedPermissions = grants; AllBranches = allBranches;
        BranchIds = allBranches ? Array.Empty<Guid>() : scope;
        Touch();
    }
    public bool Has(string permission) => IsActive && GrantedPermissions.Contains(permission, StringComparer.Ordinal);
    public bool Allows(Guid branch) => AllBranches || BranchIds.Contains(branch);
    public void SetActive(bool active) { IsActive = active; Touch(); }
}
