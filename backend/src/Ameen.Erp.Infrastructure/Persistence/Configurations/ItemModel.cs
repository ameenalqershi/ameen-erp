using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureItem(ModelBuilder model)
    {
        var item = Tenant<Item>(model, "Items");
        item.Property(x => x.Code).HasMaxLength(32); item.Property(x => x.Name).HasMaxLength(200);
        item.Property(x => x.EnglishName).HasMaxLength(200); item.Property(x => x.Notes).HasMaxLength(2000); item.Property(x => x.ReorderLevel).HasPrecision(20, 6);
        item.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        item.HasOne<ItemGroup>().WithMany().HasForeignKey(x => new { x.CompanyId, x.GroupId }).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BaseUnitId }).OnDelete(DeleteBehavior.NoAction);
        item.HasMany(x => x.Units).WithOne().HasForeignKey(x => new { x.CompanyId, x.ItemId }).OnDelete(DeleteBehavior.Cascade); item.Navigation(x => x.Units).AutoInclude();
        Constraint(item, "CK_Item_Revision", "[Revision] > 0"); Constraint(item, "CK_Item_Type", "[Type] BETWEEN 0 AND 3"); Constraint(item, "CK_Item_Tracking", "[Tracking] BETWEEN 0 AND 2");
        Constraint(item, "CK_Item_Reorder", "[ReorderLevel] IS NULL OR [ReorderLevel] >= 0");
        Constraint(item, "CK_Item_Expiry", "[ShelfLifeDays] IS NULL OR ([ShelfLifeDays] > 0 AND [Tracking] <> 0)");
        Constraint(item, "CK_Item_Service", "[Type] <> 1 OR ([Tracking] = 0 AND [ShelfLifeDays] IS NULL AND [ReorderLevel] IS NULL)");
    }
}
