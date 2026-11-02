using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureApprovalRoutes(ModelBuilder model)
    {
        var route = Tenant<ApprovalRoute>(model, "ApprovalRoutes");
        route.Property(x => x.DocumentType).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        route.Property(x => x.MinimumAmount).HasPrecision(20, 6);
        route.Property(x => x.MaximumAmount).HasPrecision(20, 6);
        route.Property(x => x.CreatedBy).HasMaxLength(200);
        route.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        route.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyId })
            .OnDelete(DeleteBehavior.NoAction);
        route.HasMany(x => x.Stages).WithOne().HasForeignKey(x => new { x.CompanyId, x.ApprovalRouteId })
            .OnDelete(DeleteBehavior.Cascade);
        route.Navigation(x => x.Stages).AutoInclude();
        Constraint(route, "CK_ApprovalRoute_Amount", "[MinimumAmount]>=0 AND ([MaximumAmount] IS NULL OR [MaximumAmount]>=[MinimumAmount])");
        Constraint(route, "CK_ApprovalRoute_Revision", "[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");

        var stage = Tenant<ApprovalRouteStage>(model, "ApprovalRouteStages");
        stage.HasIndex(x => new { x.CompanyId, x.ApprovalRouteId, x.StageNumber }).IsUnique();
        Constraint(stage, "CK_ApprovalRouteStage_Number", "[StageNumber]>0 AND ([AlertAfterMinutes] IS NULL OR [AlertAfterMinutes]>=0)");
        // SYS07 currently grants permissions to memberships, but has no persisted role catalog.
        // ApproverRoleId is structurally required; no role FK or activation is claimed.
    }
}
