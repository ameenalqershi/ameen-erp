using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

// CASH05-01 schema-only: no EF transfer mutations until authorization,
// source/target eligibility, balances, FX/fee valuation and GL06 are approved.
public sealed partial class ErpDbContext
{
    private static void ConfigureCashTransfers(ModelBuilder model)
    {
        var transfer = Tenant<CashTransfer>(model, "CashTransfers");
        transfer.Property(x => x.Number).HasMaxLength(64);
        transfer.Property(x => x.CurrencyCode).HasMaxLength(3);
        transfer.Property(x => x.DestinationCurrencyCode).HasMaxLength(3);
        transfer.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        transfer.Property(x => x.DestinationExchangeRate).HasPrecision(20, 10);
        transfer.Property(x => x.SourceAmount).HasPrecision(20, 6);
        transfer.Property(x => x.DestinationAmount).HasPrecision(20, 6);
        transfer.Property(x => x.Fee).HasPrecision(20, 6);
        transfer.Property(x => x.Description).HasMaxLength(1000);
        transfer.Property(x => x.ExternalReference).HasMaxLength(100);
        transfer.Property(x => x.State).HasMaxLength(32);
        transfer.Property(x => x.CreatedBy).HasMaxLength(200);
        transfer.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        transfer.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");

        transfer.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        transfer.HasOne<FiscalYear>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalYearId }).OnDelete(DeleteBehavior.NoAction);
        transfer.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        transfer.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DestinationCurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);

        // Explicit typed aliases enforce composite tenant-scoped FKs even
        // though the original CASH05 source/destination may be cashbox or bank.
        transfer.HasOne<CashBox>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceCashBoxId }).OnDelete(DeleteBehavior.NoAction);
        transfer.HasOne<BankAccount>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceBankAccountId }).OnDelete(DeleteBehavior.NoAction);
        transfer.HasOne<CashBox>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DestinationCashBoxId }).OnDelete(DeleteBehavior.NoAction);
        transfer.HasOne<BankAccount>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DestinationBankAccountId }).OnDelete(DeleteBehavior.NoAction);

        transfer.HasIndex(x => new { x.CompanyId, x.BranchId, x.FiscalYearId, x.Number });
        Constraint(transfer, "CK_CashTransfer_Revision", "[Revision]>0 AND [CreatedAt]<=[LastModifiedAt]");
        Constraint(transfer, "CK_CashTransfer_Money", "[SourceAmount]>0 AND [DestinationAmount]>0 AND ([Fee] IS NULL OR [Fee]>=0)");
        Constraint(transfer, "CK_CashTransfer_Rates", "[ExchangeRate]>0 AND [DestinationExchangeRate]>0");
        Constraint(transfer, "CK_CashTransfer_Draft", "[State]=N'Draft'");
        Constraint(transfer, "CK_CashTransfer_Accounts",
            "(([SourceKind]=0 AND [SourceCashBoxId] IS NOT NULL AND [SourceCashBoxId]=[SourceAccountId] AND [SourceBankAccountId] IS NULL) OR " +
            "([SourceKind]=1 AND [SourceBankAccountId] IS NOT NULL AND [SourceBankAccountId]=[SourceAccountId] AND [SourceCashBoxId] IS NULL)) AND " +
            "(([DestinationKind]=0 AND [DestinationCashBoxId] IS NOT NULL AND [DestinationCashBoxId]=[DestinationAccountId] AND [DestinationBankAccountId] IS NULL) OR " +
            "([DestinationKind]=1 AND [DestinationBankAccountId] IS NOT NULL AND [DestinationBankAccountId]=[DestinationAccountId] AND [DestinationCashBoxId] IS NULL)) AND " +
            "([SourceKind]<>[DestinationKind] OR [SourceAccountId]<>[DestinationAccountId])");
    }
}
