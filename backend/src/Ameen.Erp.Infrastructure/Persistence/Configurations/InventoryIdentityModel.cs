using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureInventoryIdentity(ModelBuilder model)
    {
        var identity = Tenant<InventoryIdentity>(model, "InventoryIdentities");
        identity.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        identity.Property(x => x.Name).HasMaxLength(200);
        identity.Property(x => x.EnglishName).HasMaxLength(200);
        identity.Property(x => x.Notes).HasMaxLength(2000);
        identity.HasIndex(x => new { x.CompanyId, x.ItemId, x.Tracking, x.Code }).IsUnique()
            .HasDatabaseName("IX_InventoryIdentities_CompanyId_ItemId_Tracking_Code");
        identity.HasIndex(x => new { x.CompanyId, x.ItemId, x.QualityStatus })
            .HasDatabaseName("IX_InventoryIdentities_CompanyId_ItemId_QualityStatus");
        identity.HasOne<Item>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ItemId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(identity, "CK_InventoryIdentity_Revision", "[Revision] > 0");
        Constraint(identity, "CK_InventoryIdentity_Tracking", "[Tracking] IN (1, 2)");
        Constraint(identity, "CK_InventoryIdentity_Quality", "[QualityStatus] BETWEEN 0 AND 2");
        Constraint(identity, "CK_InventoryIdentity_Expiry",
            "[ProductionDate] IS NULL OR [ExpiryDate] IS NULL OR [ExpiryDate] > [ProductionDate]");
    }
}
