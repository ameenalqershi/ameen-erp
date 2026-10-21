using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureBranch(ModelBuilder model)
    {
        var branch = Tenant<Branch>(model, "Branches");
        branch.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        branch.Property(x => x.Code).HasMaxLength(32); branch.Property(x => x.Name).HasMaxLength(200);
        branch.Property(x => x.EnglishName).HasMaxLength(200); branch.Property(x => x.Notes).HasMaxLength(2000);
        branch.Property(x => x.City).HasMaxLength(200); branch.Property(x => x.Address).HasMaxLength(500);
        branch.Ignore(x => x.IsProfileComplete); Constraint(branch, "CK_Branch_Revision", "[Revision] > 0");
        branch.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId }).OnDelete(DeleteBehavior.NoAction);
        branch.HasOne<CostCenter>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DefaultCostCenterId }).OnDelete(DeleteBehavior.NoAction);
    }
}
