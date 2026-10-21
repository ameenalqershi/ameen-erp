using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureJournalLine(ModelBuilder model)
    {
        var line = Tenant<JournalLine>(model, "JournalLines");
        line.Property(x => x.Description).HasMaxLength(500); line.Property(x => x.CurrencyCode).HasMaxLength(3);
        line.Property(x => x.Debit).HasPrecision(20, 6); line.Property(x => x.Credit).HasPrecision(20, 6);
        line.Property(x => x.FunctionalDebit).HasPrecision(20, 6); line.Property(x => x.FunctionalCredit).HasPrecision(20, 6);
        line.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        line.HasIndex(x => new { x.CompanyId, x.JournalId, x.Position }).IsUnique();
        line.HasIndex(x => new { x.CompanyId, x.AccountId, x.JournalId });
        line.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccountId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<CostCenter>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostCenterId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<ExchangeRate>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExchangeRateId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(line, "CK_Line_Amounts", "([Debit] > 0 AND [Credit] = 0 AND [FunctionalDebit] > 0 AND [FunctionalCredit] = 0) OR ([Credit] > 0 AND [Debit] = 0 AND [FunctionalCredit] > 0 AND [FunctionalDebit] = 0)");
        Constraint(line, "CK_Line_Rate", "[ExchangeRate] > 0");
    }
}
