using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureSecurityRoles(ModelBuilder model)
    {
        var role = Tenant<SecurityRole>(model, "SecurityRoles");
        role.Property(x => x.Name).HasMaxLength(200);
        role.Property(x => x.Responsibility).HasMaxLength(500);
        role.Property(x => x.CreatedBy).HasMaxLength(200);
        role.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        role.HasMany(x => x.Rules).WithOne().HasForeignKey(x => new { x.CompanyId, x.RoleId })
            .OnDelete(DeleteBehavior.Cascade);
        role.Navigation(x => x.Rules).AutoInclude();
        Constraint(role, "CK_SecurityRole_Revision", "[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");

        var rule = Tenant<SecurityRoleRule>(model, "SecurityRoleRules");
        rule.Property(x => x.ScreenCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        rule.Property(x => x.OperationCode).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
        rule.Property(x => x.AmountLimit).HasPrecision(20, 6);
        rule.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyId })
            .OnDelete(DeleteBehavior.NoAction);
        rule.HasIndex(x => new { x.CompanyId, x.RoleId, x.ScreenCode, x.OperationCode, x.CurrencyId }).IsUnique();
        Constraint(rule, "CK_SecurityRoleRule_Limit",
            "([AmountLimit] IS NULL AND [CurrencyId] IS NULL) OR ([Allowed]=1 AND [AmountLimit]>=0 AND [CurrencyId] IS NOT NULL)");

        var assignment = Tenant<SecurityRoleAssignment>(model, "SecurityRoleAssignments");
        assignment.HasOne<Membership>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MembershipId }).OnDelete(DeleteBehavior.NoAction);
        assignment.HasOne<SecurityRole>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RoleId }).OnDelete(DeleteBehavior.NoAction);
        assignment.HasIndex(x => new { x.CompanyId, x.MembershipId, x.RoleId }).IsUnique();
    }
}
