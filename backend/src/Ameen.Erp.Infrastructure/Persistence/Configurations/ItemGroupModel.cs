using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureItemGroup(ModelBuilder model)
    {
        var itemGroup = Tenant<ItemGroup>(model, "ItemGroups");
        itemGroup.Property(x => x.Code).HasMaxLength(32); itemGroup.Property(x => x.Name).HasMaxLength(200);
        itemGroup.Property(x => x.EnglishName).HasMaxLength(200); itemGroup.Property(x => x.Notes).HasMaxLength(2000);
        itemGroup.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        itemGroup.HasOne<ItemGroup>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId }).OnDelete(DeleteBehavior.NoAction);
        itemGroup.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SalesAccountId }).OnDelete(DeleteBehavior.NoAction);
        itemGroup.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InventoryAccountId }).OnDelete(DeleteBehavior.NoAction);
        itemGroup.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostAccountId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(itemGroup, "CK_ItemGroup_Revision", "[Revision] > 0");
    }
}
