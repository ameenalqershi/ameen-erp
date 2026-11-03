using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureBankAccount(ModelBuilder model)
    {
        var bank = Tenant<BankAccount>(model, "BankAccounts");
        bank.Property(x => x.Code).HasMaxLength(32);
        bank.Property(x => x.Name).HasMaxLength(200);
        bank.Property(x => x.EnglishName).HasMaxLength(200);
        bank.Property(x => x.Notes).HasMaxLength(2000);
        bank.Property(x => x.BankName).HasMaxLength(200);
        bank.Property(x => x.AccountNumber).HasMaxLength(64);
        bank.Property(x => x.Iban).HasMaxLength(34);
        bank.Property(x => x.CurrencyCode).HasMaxLength(3);
        bank.Property(x => x.CreatedBy).HasMaxLength(200);
        bank.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        bank.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        bank.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        bank.HasIndex(x => new { x.CompanyId, x.BankName, x.AccountNumber }).IsUnique();
        bank.HasIndex(x => new { x.CompanyId, x.Iban }).IsUnique().HasFilter("[Iban] IS NOT NULL");
        bank.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.LedgerAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        bank.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        bank.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(bank, "CK_BankAccount_Revision", "[Revision] > 0");
        Constraint(bank, "CK_BankAccount_AuditTime", "[CreatedAt] <= [LastModifiedAt]");
        Constraint(bank, "CK_BankAccount_IbanLength", "[Iban] IS NULL OR LEN([Iban]) BETWEEN 15 AND 34");
    }
}
