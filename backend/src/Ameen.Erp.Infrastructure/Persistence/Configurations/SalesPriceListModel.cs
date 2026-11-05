using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureSalesPriceLists(ModelBuilder model)
    {
        var lists = Tenant<SalesPriceList>(model, "SalesPriceLists");
        lists.Ignore(x => x.Lines); // Lines are queried through the explicit child set.
        lists.Property(x => x.Status).HasMaxLength(64);
        lists.Property(x => x.Name).HasMaxLength(200);
        lists.Property(x => x.CreatedBy).HasMaxLength(200);
        lists.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        lists.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        lists.Property(x => x.EffectiveFrom).HasColumnType("date");
        lists.Property(x => x.EffectiveTo).HasColumnType("date");
        lists.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        lists.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CurrencyId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(lists, "CK_SalesPriceList_Audit",
            "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");
        Constraint(lists, "CK_SalesPriceList_Dates",
            "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
        Constraint(lists, "CK_SalesPriceList_References",
            "[CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "([BranchId] IS NULL OR [BranchId] <> '00000000-0000-0000-0000-000000000000')");

        var lines = Tenant<SalesPriceListLine>(model, "SalesPriceListLines");
        lines.Property(x => x.Price).HasPrecision(20, 6);
        lines.Property(x => x.DiscountLimit).HasPrecision(20, 6);
        lines.Property(x => x.QuantityThreshold).HasPrecision(20, 6);
        lines.HasIndex(x => new { x.CompanyId, x.PriceListId, x.Position }).IsUnique();
        lines.HasOne<SalesPriceList>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PriceListId })
            .OnDelete(DeleteBehavior.NoAction);
        // ItemUnit already has a unique (CompanyId, ItemId, UnitId) index;
        // use that sanctioned provider key to reject an unrelated unit for an item.
        lines.HasOne<ItemUnit>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemId, x.UnitId })
            .HasPrincipalKey(x => new { x.CompanyId, x.ItemId, x.UnitId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(lines, "CK_SalesPriceListLine_Position",
            "[Position] > 0");
        Constraint(lines, "CK_SalesPriceListLine_Amounts",
            "[Price] >= 0 AND [DiscountLimit] >= 0 AND " +
            "([QuantityThreshold] IS NULL OR [QuantityThreshold] >= 0)");
        Constraint(lines, "CK_SalesPriceListLine_References",
            "[PriceListId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "[ItemId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "[UnitId] <> '00000000-0000-0000-0000-000000000000'");
    }
}
