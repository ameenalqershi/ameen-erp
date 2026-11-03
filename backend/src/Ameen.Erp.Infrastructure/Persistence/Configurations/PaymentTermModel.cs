using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigurePaymentTerms(ModelBuilder model)
    {
        var terms = Tenant<PaymentTerm>(model, "PaymentTerms");
        terms.Property(x => x.Code).HasMaxLength(32);
        terms.Property(x => x.Name).HasMaxLength(200);
        terms.Property(x => x.CreatedBy).HasMaxLength(200);
        terms.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        terms.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        terms.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        terms.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SettlementAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(terms, "CK_PaymentTerm_Kind", "[Kind] BETWEEN 0 AND 2");
        Constraint(terms, "CK_PaymentTerm_Grace", "[GraceDays] >= 0");
        Constraint(terms, "CK_PaymentTerm_Audit", "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");
        terms.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.CompanyId, x.TermId })
            .OnDelete(DeleteBehavior.NoAction);

        var lines = Tenant<PaymentTermLine>(model, "PaymentTermLines");
        lines.Property(x => x.Percent).HasPrecision(9, 6);
        lines.HasIndex(x => new { x.CompanyId, x.TermId, x.Position }).IsUnique();
        Constraint(lines, "CK_PaymentTermLine_Percent", "[Percent] > 0 AND [Percent] <= 100");
        Constraint(lines, "CK_PaymentTermLine_Days", "[DaysFromBase] >= 0 AND [Position] > 0");
    }
}
