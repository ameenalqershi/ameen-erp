namespace Ameen.Erp.Domain;

public sealed class Membership : CompanyEntity
{
    public string Subject { get; private set; } = "";
    public string[] GrantedPermissions { get; private set; } = Array.Empty<string>();
    public Guid[] BranchIds { get; private set; } = Array.Empty<Guid>();
    public bool AllBranches { get; private set; }
    public bool IsActive { get; private set; } = true;
    private Membership() { }
    public Membership(Guid companyId, string subject, IEnumerable<string> permissions,
        bool allBranches, IEnumerable<Guid> branches) : base(companyId)
    {
        Subject = Check.Text(subject, "معرف المستخدم لدى جهة الهوية");
        Configure(permissions, allBranches, branches);
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
