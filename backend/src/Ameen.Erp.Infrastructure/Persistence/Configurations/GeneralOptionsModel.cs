using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureGeneralOptions(ModelBuilder model)
    {
        var policy = Tenant<GeneralOptionsPolicy>(model, "GeneralOptionsPolicies");
        policy.Property(x => x.StatusCode).HasMaxLength(32);
        policy.Property(x => x.InventoryValuationCode).HasMaxLength(64);
        policy.Property(x => x.SalesDeliveryModeCode).HasMaxLength(64);
        policy.Property(x => x.CreatedBy).HasMaxLength(200);
        policy.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        policy.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        policy.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        policy.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => new { x.CompanyId, x.PolicyId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(policy, "CK_GeneralOptions_Precision", "[QuantityPrecision] >= 0");
        Constraint(policy, "CK_GeneralOptions_Audit",
            "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");

        var line = Tenant<GeneralOptionsAccountLine>(model, "GeneralOptionsAccountLines");
        line.Property(x => x.EventCode).HasMaxLength(64);
        line.Property(x => x.AccountRoleCode).HasMaxLength(64);
        line.Property(x => x.Scope).HasMaxLength(200);
        line.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AccountId })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
