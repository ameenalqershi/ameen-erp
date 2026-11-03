using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SYS08-FG01/FG03 scope fields, without any operational grant or read.
public static class UserDataScopeFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS08: user/role scope structure retains tenant, audited header and branch dimension", f =>
        {
            var now=new FixedClock().UtcNow;
            var member=Guid.NewGuid();
            var role=Guid.NewGuid();
            var beforeAudit=f.Store.Count<AuditEvent>();
            var beforeJournals=f.Store.Count<JournalEntry>();
            var scope=new UserDataScope(f.CompanyId, f.BranchId, "unapproved-status",
                member, role, f.Owner.Subject, now,
                new[] { (f.BranchId, (Guid?)null, (Guid?)null, (Guid?)null, (Guid?)null) });
            Ensure(scope.CompanyId==f.CompanyId && scope.BranchId==f.BranchId &&
                scope.MembershipId==member && scope.RoleId==role &&
                scope.StatusCode=="unapproved-status" && scope.Revision==1 &&
                scope.CreatedBy==f.Owner.Subject && scope.CreatedAt==now &&
                scope.LastModifiedAt==now && scope.Lines.Count==1 &&
                scope.Lines[0].CompanyId==f.CompanyId &&
                scope.Lines[0].ScopeId==scope.Id &&
                scope.Lines[0].BranchId==f.BranchId &&
                scope.Lines[0].WarehouseId is null &&
                scope.Lines[0].CashBoxId is null &&
                scope.Lines[0].AccountGroupId is null &&
                scope.Lines[0].CustomerGroupId is null,
                "SYS08 source fields or tenant metadata lost");
            Ensure(f.Store.Count<UserDataScope>()==0 &&
                f.Store.Count<UserDataScopeLine>()==0 &&
                f.Store.Count<AuditEvent>()==beforeAudit &&
                f.Store.Count<JournalEntry>()==beforeJournals,
                "Schema-only SYS08 scope changed authorized state or history");
            return Task.CompletedTask;
        });

        test("SYS08: empty links and missing group catalogues fail closed with no rights", f =>
        {
            var now=new FixedClock().UtcNow;
            var member=Guid.NewGuid();var role=Guid.NewGuid();
            var line=new[] { (f.BranchId, (Guid?)null, (Guid?)null, (Guid?)null, (Guid?)null) };
            UserDataScope Create(Guid cid,Guid? branch,Guid m,Guid r,
                IReadOnlyList<(Guid,Guid?,Guid?,Guid?,Guid?)> l) =>
                new(cid,branch,"unapproved-status",m,r,f.Owner.Subject,now,l);
            Error("company_required",()=>Create(Guid.Empty,f.BranchId,member,role,line));
            Error("data_scope_reference_invalid",()=>Create(f.CompanyId,Guid.Empty,member,role,line));
            Error("data_scope_reference_invalid",()=>Create(f.CompanyId,null,Guid.Empty,role,line));
            Error("data_scope_reference_invalid",()=>Create(f.CompanyId,null,member,Guid.Empty,line));
            Error("data_scope_lines_required",()=>Create(f.CompanyId,null,member,role,
                Array.Empty<(Guid,Guid?,Guid?,Guid?,Guid?)>()));
            Error("data_scope_line_reference_invalid",()=>new UserDataScopeLine(
                f.CompanyId,Guid.Empty,f.BranchId,null,null,null,null));
            Error("data_scope_line_reference_invalid",()=>new UserDataScopeLine(
                f.CompanyId,Guid.NewGuid(),Guid.Empty,null,null,null,null));
            Error("data_scope_group_catalog_unavailable",()=>new UserDataScopeLine(
                f.CompanyId,Guid.NewGuid(),f.BranchId,null,null,Guid.NewGuid(),null));
            Error("data_scope_group_catalog_unavailable",()=>new UserDataScopeLine(
                f.CompanyId,Guid.NewGuid(),f.BranchId,null,null,null,Guid.NewGuid()));
            Ensure(f.Store.Count<UserDataScope>()==0 &&
                f.Store.Count<UserDataScopeLine>()==0,
                "Invalid SYS08 scope mutated tracked state");
            return Task.CompletedTask;
        });
    }
    private static void Error(string code,Action action)
    {
        try { action(); }
        catch(BusinessException ex) when(ex.Code==code) {return;}
        throw new Exception("Expected SYS08 error "+code);
    }
    private static void Ensure(bool valid,string message)
    {
        if(!valid) throw new Exception(message);
    }
}
