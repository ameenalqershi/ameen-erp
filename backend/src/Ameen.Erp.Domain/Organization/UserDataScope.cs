namespace Ameen.Erp.Domain;

// SYS08 scope metadata foundation only. It does not modify SYS07 role grants
// or enable access checks. Authorization remains DENIED for these new scopes
// until an approved snapshot policy, group catalogues and API are implemented.
public sealed class UserDataScope : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<UserDataScopeLine> Lines { get; private set; } = new();

    private UserDataScope() { }
    public UserDataScope(Guid companyId, Guid? branchId, string statusCode,
        Guid membershipId, Guid roleId, string createdBy, DateTimeOffset now,
        IReadOnlyList<(Guid BranchId, Guid? WarehouseId, Guid? CashBoxId,
            Guid? AccountGroupId, Guid? CustomerGroupId)> lines) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && membershipId != Guid.Empty &&
            roleId != Guid.Empty, "data_scope_reference_invalid",
            "الشركة والعضوية والدور وفرع النطاق يجب أن تكون مراجع صحيحة.");
        Check.That(lines is { Count: > 0 }, "data_scope_lines_required",
            "يجب تحديد فرع واحد على الأقل للنطاق.");
        BranchId = branchId;
        StatusCode = Check.Text(statusCode, "حالة النطاق", 32);
        MembershipId = membershipId;
        RoleId = roleId;
        CreatedBy = Check.Text(createdBy, "منشئ النطاق", 200);
        CreatedAt = LastModifiedAt = now;
        Lines = lines!.Select(x => new UserDataScopeLine(companyId, Id,
            x.BranchId, x.WarehouseId, x.CashBoxId, x.AccountGroupId,
            x.CustomerGroupId)).ToList();
    }
}

public sealed class UserDataScopeLine : CompanyEntity
{
    public Guid ScopeId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? WarehouseId { get; private set; }
    public Guid? CashBoxId { get; private set; }
    public Guid? AccountGroupId { get; private set; }
    public Guid? CustomerGroupId { get; private set; }

    private UserDataScopeLine() { }
    public UserDataScopeLine(Guid companyId, Guid scopeId, Guid branchId,
        Guid? warehouseId, Guid? cashBoxId, Guid? accountGroupId,
        Guid? customerGroupId) : base(companyId)
    {
        Check.That(scopeId != Guid.Empty && branchId != Guid.Empty &&
            warehouseId != Guid.Empty && cashBoxId != Guid.Empty &&
            accountGroupId != Guid.Empty && customerGroupId != Guid.Empty,
            "data_scope_line_reference_invalid", "مرجع نطاق البيانات غير صالح.");
        // Neither an account group nor a customer group exists as a verified
        // tenant-scoped catalogue in the current repository. No unchecked
        // GUID can be mistaken for a validated authorization dimension.
        Check.That(accountGroupId is null && customerGroupId is null,
            "data_scope_group_catalog_unavailable",
            "نطاق مجموعات الحسابات والعملاء محجوب حتى اعتماد بطاقة المجموعة.");
        ScopeId = scopeId;
        BranchId = branchId;
        WarehouseId = warehouseId;
        CashBoxId = cashBoxId;
    }
}
