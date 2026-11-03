using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCashReceipts(ModelBuilder model)
    {
        var receipt = Tenant<CashReceipt>(model, "CashReceipts");
        receipt.Property(x => x.Number).HasMaxLength(64);
        receipt.Property(x => x.CurrencyCode).HasMaxLength(3);
        receipt.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        receipt.Property(x => x.Description).HasMaxLength(1000);
        receipt.Property(x => x.ExternalReference).HasMaxLength(100);
        receipt.Property(x => x.State).HasMaxLength(32);
        receipt.Property(x => x.PaymentMethod).HasMaxLength(32);
        receipt.Property(x => x.ChequeReference).HasMaxLength(100);
        receipt.Property(x => x.Amount).HasPrecision(20, 6);
        receipt.Property(x => x.CreatedBy).HasMaxLength(200);
        receipt.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        receipt.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        receipt.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        receipt.HasOne<FiscalYear>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalYearId })
            .OnDelete(DeleteBehavior.NoAction);
        receipt.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        receipt.HasOne<CashBox>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CashBoxId })
            .OnDelete(DeleteBehavior.NoAction);
        receipt.HasOne<BankAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        receipt.HasIndex(x => new { x.CompanyId, x.BranchId, x.FiscalYearId, x.Number });
        Constraint(receipt, "CK_CashReceipt_Revision", "[Revision]>0");
        Constraint(receipt, "CK_CashReceipt_AuditTime", "[CreatedAt]<=[LastModifiedAt]");
        Constraint(receipt, "CK_CashReceipt_Rate", "[ExchangeRate]>0");
        Constraint(receipt, "CK_CashReceipt_Amount", "[Amount]>0");
        Constraint(receipt, "CK_CashReceipt_Nonempty",
            "LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([PaymentMethod])))>0");

        var line = Tenant<CashReceiptLine>(model, "CashReceiptLines");
        line.Property(x => x.AllocatedAmount).HasPrecision(20, 6);
        line.Property(x => x.SettlementDiscount).HasPrecision(20, 6);
        line.HasOne<CashReceipt>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReceiptId }).OnDelete(DeleteBehavior.NoAction);
        line.HasIndex(x => new { x.CompanyId, x.ReceiptId });
        Constraint(line, "CK_CashReceiptLine_Amounts",
            "[AllocatedAmount]>=0 AND [SettlementDiscount]>=0");
    }
}
