using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Infrastructure;
public sealed partial class ErpDbContext
{
    private static void ConfigureInventoryOpening(ModelBuilder model)
    {
        var document = Tenant<InventoryOpening>(model, "InventoryOpenings");
        document.Property(x => x.Number).HasMaxLength(64); document.Property(x => x.CurrencyCode).HasMaxLength(3);
        document.Property(x => x.ExchangeRate).HasPrecision(20, 10); document.Property(x => x.Description).HasMaxLength(500);
        document.Property(x => x.CountReference).HasMaxLength(100); document.Property(x => x.ExternalReference).HasMaxLength(100);
        document.Property(x => x.CreatedBy).HasMaxLength(200); document.Property(x => x.ApprovedBy).HasMaxLength(200);
        document.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        document.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.State });
        document.HasIndex(x => new { x.CompanyId, x.BranchId, x.AccountingDate });
        document.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.CompanyId, x.WarehouseId }).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<FiscalYear>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalYearId }).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId }).OnDelete(DeleteBehavior.NoAction);
        document.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode }).HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        document.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.CompanyId, x.OpeningId }).OnDelete(DeleteBehavior.Cascade);
        document.Navigation(x => x.Lines).AutoInclude();
        Constraint(document, "CK_InventoryOpening_State", "[State] BETWEEN 0 AND 5");
        Constraint(document, "CK_InventoryOpening_Revision", "[Revision] > 0");
        Constraint(document, "CK_InventoryOpening_Rate", "[ExchangeRate] = 1");
        Constraint(document, "CK_InventoryOpening_Approval", "([State] IN (3,5) AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> [CreatedBy]) OR ([State] NOT IN (3,5) AND [ApprovedBy] IS NULL)");
        var line = Tenant<InventoryOpeningLine>(model, "InventoryOpeningLines");
        line.Property(x => x.ItemCode).HasMaxLength(32); line.Property(x => x.UnitCode).HasMaxLength(32);
        line.Property(x => x.LotNumber).HasMaxLength(32); line.Property(x => x.SerialNumber).HasMaxLength(32);
        line.Property(x => x.Quantity).HasPrecision(20, 6); line.Property(x => x.FactorToBase).HasPrecision(20, 6);
        line.Property(x => x.BaseQuantity).HasPrecision(20, 6); line.Property(x => x.UnitCost).HasPrecision(20, 6); line.Property(x => x.Value).HasPrecision(20, 6);
        line.HasIndex(x => new { x.CompanyId, x.OpeningId, x.Position }).IsUnique();
        line.HasOne<Item>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ItemId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => new { x.CompanyId, x.UnitId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<InventoryIdentity>().WithMany().HasForeignKey(x => new { x.CompanyId, x.IdentityId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(line, "CK_InventoryOpeningLine_Values", "[Position] > 0 AND [Quantity] > 0 AND [FactorToBase] > 0 AND [BaseQuantity] > 0 AND [UnitCost] >= 0 AND [Value] >= 0");
    }
}
