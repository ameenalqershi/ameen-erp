using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Infrastructure;
public sealed partial class ErpDbContext
{
    private static void ConfigureWarehouses(ModelBuilder model)
    {
        var w = Tenant<Warehouse>(model, "Warehouses");
        w.Property(x => x.Code).HasMaxLength(32); w.Property(x => x.Name).HasMaxLength(200);
        w.Property(x => x.EnglishName).HasMaxLength(200); w.Property(x => x.Notes).HasMaxLength(2000);
        w.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        w.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        w.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.CompanyId, x.KeeperMembershipId }).OnDelete(DeleteBehavior.NoAction);
        w.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InventoryAccountId }).OnDelete(DeleteBehavior.NoAction);
        w.HasMany(x => x.Locations).WithOne().HasForeignKey(x => new { x.CompanyId, x.WarehouseId }).OnDelete(DeleteBehavior.Cascade);
        w.Navigation(x => x.Locations).AutoInclude();
        Constraint(w, "CK_Warehouse_Revision", "[Revision] > 0"); Constraint(w, "CK_Warehouse_Type", "[Type] BETWEEN 0 AND 1");
        var l = Tenant<WarehouseLocation>(model, "WarehouseLocations");
        l.Property(x => x.Code).HasMaxLength(32); l.Property(x => x.Description).HasMaxLength(500);
        l.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.Code }).IsUnique();
    }
}
