using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCashBox(ModelBuilder model)
    {
        var box = Tenant<CashBox>(model, "CashBoxes");
        box.Property(x => x.Code).HasMaxLength(32);
        box.Property(x => x.Name).HasMaxLength(200);
        box.Property(x => x.EnglishName).HasMaxLength(200);
        box.Property(x => x.Notes).HasMaxLength(2000);
        box.Property(x => x.CurrencyCode).HasMaxLength(3);
        box.Property(x => x.CreatedBy).HasMaxLength(200);
        box.Property(x => x.BalanceLimit).HasPrecision(20, 6);
        box.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        box.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        box.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        // Account may be reused only with a distinct branch/currency dimension.
        box.HasIndex(x => new { x.CompanyId, x.AccountId, x.BranchId, x.CurrencyCode }).IsUnique();
        box.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccountId })
            .OnDelete(DeleteBehavior.NoAction);
        box.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        box.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CustodianMembershipId })
            .OnDelete(DeleteBehavior.NoAction);
        box.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(box, "CK_CashBox_Revision", "[Revision] > 0");
        Constraint(box, "CK_CashBox_BalanceLimit", "[BalanceLimit] IS NULL OR [BalanceLimit] >= 0");
        Constraint(box, "CK_CashBox_AuditTime", "[CreatedAt] <= [LastModifiedAt]");
    }
}
