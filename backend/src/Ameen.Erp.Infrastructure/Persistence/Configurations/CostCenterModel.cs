using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCostCenter(ModelBuilder model)
    {
        var center = Tenant<CostCenter>(model, "CostCenters");
        center.Property(x => x.Code).HasMaxLength(32); center.Property(x => x.Name).HasMaxLength(200);
        center.Property(x => x.EnglishName).HasMaxLength(200); center.Property(x => x.Notes).HasMaxLength(2000);
        center.HasOne<CostCenter>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId }).OnDelete(DeleteBehavior.NoAction);
        center.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ResponsibleMembershipId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(center, "CK_CostCenter_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
        Constraint(center, "CK_CostCenter_Revision", "[Revision] > 0");
        center.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        center.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
    }
}
