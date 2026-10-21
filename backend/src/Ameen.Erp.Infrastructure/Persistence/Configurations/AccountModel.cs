using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureAccount(ModelBuilder model)
    {
        var account = Tenant<Account>(model, "Accounts");
        account.Property(x => x.Code).HasMaxLength(32); account.Property(x => x.Name).HasMaxLength(200);
        account.Property(x => x.EnglishName).HasMaxLength(200); account.Property(x => x.Notes).HasMaxLength(2000);
        account.Property(x => x.CurrencyCode).HasMaxLength(3);
        account.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        account.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId }).OnDelete(DeleteBehavior.NoAction);
        account.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(account, "CK_Account_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
        Constraint(account, "CK_Account_NormalBalance", "[NormalBalance] BETWEEN 0 AND 1");
        Constraint(account, "CK_Account_Revision", "[Revision] > 0");
    }
}
