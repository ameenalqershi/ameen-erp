namespace Ameen.Erp.Domain;

// SYS08 user-to-SYS07 role reference. Persistence is intentionally blocked until
// role administration, authorization and audit acceptance are complete.
public sealed class SecurityRoleAssignment : CompanyEntity
{
    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }

    private SecurityRoleAssignment() { }

    public SecurityRoleAssignment(Guid companyId, Guid membershipId, Guid roleId) : base(companyId)
    {
        Check.That(membershipId != Guid.Empty && roleId != Guid.Empty,
            "invalid_role_assignment", "المستخدم والدور مطلوبان.");
        MembershipId = membershipId;
        RoleId = roleId;
    }
}

// Pure fail-closed evaluator: does not authorize requests or replace the current
// membership grant system. A future service must load complete assignments and roles
// in one tenant-scoped snapshot before invoking this policy.
public static class SecurityRolePolicyEvaluator
{
    public static bool Permits(Membership member, IEnumerable<SecurityRoleAssignment> assignments,
        IEnumerable<SecurityRole> roles, Guid companyId, Guid branchId,
        string screen, string operation, decimal? amount, Guid? currencyId)
    {
        if (member is null || assignments is null || roles is null ||
            companyId == Guid.Empty || branchId == Guid.Empty ||
            member.CompanyId != companyId || !member.IsActive || !member.Allows(branchId) ||
            string.IsNullOrWhiteSpace(screen) || string.IsNullOrWhiteSpace(operation) ||
            amount < 0)
            return false;

        var assignedIds = assignments
            .Where(x => x.CompanyId == companyId && x.MembershipId == member.Id)
            .Select(x => x.RoleId).ToHashSet();
        var applicable = roles.Where(x => assignedIds.Contains(x.Id) &&
            x.CompanyId == companyId && x.IsActive &&
            (x.BranchId is null || x.BranchId == branchId)).ToArray();
        var normalizedScreen = screen.ToUpperInvariant();
        var normalizedOperation = operation.ToUpperInvariant();

        // Denial is independent of amount/currency: a denial cannot carry a cap.
        if (applicable.Any(x => x.Rules.Any(r => r.CompanyId == companyId &&
            !r.Allowed && r.ScreenCode == normalizedScreen &&
            r.OperationCode == normalizedOperation)))
            return false;
        return applicable.Any(x => x.Permits(normalizedScreen, normalizedOperation, amount, currencyId));
    }
}
