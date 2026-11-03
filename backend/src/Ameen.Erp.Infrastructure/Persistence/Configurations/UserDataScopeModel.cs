using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureUserDataScopes(ModelBuilder model)
    {
        var scope = Tenant<UserDataScope>(model, "UserDataScopes");
        scope.Property(x => x.StatusCode).HasMaxLength(32);
        scope.Property(x => x.CreatedBy).HasMaxLength(200);
        scope.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        scope.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        scope.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        scope.HasOne<Membership>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MembershipId })
            .OnDelete(DeleteBehavior.NoAction);
        scope.HasOne<SecurityRole>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RoleId })
            .OnDelete(DeleteBehavior.NoAction);
        scope.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => new { x.CompanyId, x.ScopeId })
            .OnDelete(DeleteBehavior.NoAction);
        scope.Navigation(x => x.Lines).AutoInclude();
        Constraint(scope, "CK_UserDataScope_Audit",
            "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");

        var line = Tenant<UserDataScopeLine>(model, "UserDataScopeLines");
        line.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        line.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.WarehouseId })
            .OnDelete(DeleteBehavior.NoAction);
        line.HasOne<CashBox>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CashBoxId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(line, "CK_UserDataScopeLine_UnapprovedGroups",
            "[AccountGroupId] IS NULL AND [CustomerGroupId] IS NULL");
    }
}
