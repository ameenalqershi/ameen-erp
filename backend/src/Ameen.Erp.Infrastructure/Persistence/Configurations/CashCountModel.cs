using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCashCounts(ModelBuilder model)
    {
        var count = Tenant<CashCount>(model, "CashCounts");
        count.Property(x => x.State).HasMaxLength(32);
        count.Property(x => x.CreatedBy).HasMaxLength(200);
        count.Property(x => x.CutoffAt).HasColumnType("datetimeoffset(7)");
        count.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        count.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        count.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        count.HasOne<CashBox>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CashBoxId })
            .OnDelete(DeleteBehavior.NoAction);
        count.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CountedByMembershipId })
            .OnDelete(DeleteBehavior.NoAction);
        count.HasIndex(x => new { x.CompanyId, x.CashBoxId, x.CutoffAt });
        Constraint(count, "CK_CashCount_Revision", "[Revision] > 0");
        Constraint(count, "CK_CashCount_AuditTime", "[CreatedAt] <= [LastModifiedAt]");
        Constraint(count, "CK_CashCount_StateNonempty", "LEN(LTRIM(RTRIM([State]))) > 0");

        var line = Tenant<CashCountLine>(model, "CashCountLines");
        line.Property(x => x.Denomination).HasPrecision(20, 6);
        line.Property(x => x.CountedValue).HasComputedColumnSql(
            "CONVERT(decimal(20,6), [Denomination] * [PieceCount])", stored: true);
        line.HasOne<CashCount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CashCountId })
            .OnDelete(DeleteBehavior.NoAction);
        line.HasIndex(x => new { x.CompanyId, x.CashCountId });
        Constraint(line, "CK_CashCountLine_Values", "[Denomination] > 0 AND [PieceCount] >= 0");
    }
}
