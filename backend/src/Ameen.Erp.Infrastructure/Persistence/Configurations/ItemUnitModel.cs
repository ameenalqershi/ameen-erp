using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureItemUnit(ModelBuilder model)
    {
        var itemUnit = Tenant<ItemUnit>(model, "ItemUnits");
        itemUnit.Property(x => x.FactorToBase).HasPrecision(20, 6); itemUnit.Property(x => x.SalePrice).HasPrecision(20, 6);
        itemUnit.Property(x => x.Barcode).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
        itemUnit.HasIndex(x => new { x.CompanyId, x.ItemId, x.UnitId }).IsUnique();
        itemUnit.HasIndex(x => new { x.CompanyId, x.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        itemUnit.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => new { x.CompanyId, x.UnitId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(itemUnit, "CK_ItemUnit_Factor", "[FactorToBase] > 0"); Constraint(itemUnit, "CK_ItemUnit_Price", "[SalePrice] IS NULL OR [SalePrice] >= 0");
    }
}
