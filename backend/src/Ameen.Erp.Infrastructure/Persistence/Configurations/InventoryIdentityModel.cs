using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureInventoryIdentity(ModelBuilder model)
    {
        var identity = Tenant<InventoryIdentity>(model, "InventoryIdentities");
        identity.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        identity.Property(x => x.LotNumber).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        identity.Property(x => x.SerialNumber).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        identity.HasIndex(x => new { x.CompanyId, x.ItemId, x.LotNumber }).IsUnique().HasFilter("[LotNumber] IS NOT NULL");
        identity.HasIndex(x => new { x.CompanyId, x.ItemId, x.SerialNumber }).IsUnique().HasFilter("[SerialNumber] IS NOT NULL");
        Constraint(identity, "CK_InventoryIdentity_Number", "([Tracking] = 1 AND [LotNumber] IS NOT NULL AND LEN([LotNumber]) > 0 AND [SerialNumber] IS NULL) OR ([Tracking] = 2 AND [SerialNumber] IS NOT NULL AND LEN([SerialNumber]) > 0 AND [LotNumber] IS NULL)");
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
