using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCashAdvances(ModelBuilder model)
    {
        var header = Tenant<CashAdvance>(model, "CashAdvances");
        header.Property(x => x.Number).HasMaxLength(64);
        header.Property(x => x.CurrencyCode).HasMaxLength(3);
        header.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        header.Property(x => x.Description).HasMaxLength(1000);
        header.Property(x => x.ExternalReference).HasMaxLength(100);
        header.Property(x => x.State).HasMaxLength(32);
        header.Property(x => x.AdvanceType).HasMaxLength(32);
        header.Property(x => x.Amount).HasPrecision(20, 6);
        header.Property(x => x.CreatedBy).HasMaxLength(200);
        header.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        header.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        header.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        header.HasOne<FiscalYear>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalYearId }).OnDelete(DeleteBehavior.NoAction);
        header.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code })
            .OnDelete(DeleteBehavior.NoAction);
        header.HasIndex(x => new { x.CompanyId, x.BranchId, x.FiscalYearId, x.Number });
        Constraint(header, "CK_CashAdvance_Revision", "[Revision]>0");
        Constraint(header, "CK_CashAdvance_AuditTime", "[CreatedAt]<=[LastModifiedAt]");
        Constraint(header, "CK_CashAdvance_Rate", "[ExchangeRate]>0");
        Constraint(header, "CK_CashAdvance_Amount", "[Amount]>0");
        Constraint(header, "CK_CashAdvance_Text",
            "LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([AdvanceType])))>0");

        var line = Tenant<CashAdvanceLine>(model, "CashAdvanceLines");
        line.Property(x => x.SettlementAmount).HasPrecision(20, 6);
        line.Property(x => x.AttachmentReference).HasMaxLength(300);
        line.HasOne<CashAdvance>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AdvanceId })
            .OnDelete(DeleteBehavior.NoAction);
        line.HasIndex(x => new { x.CompanyId, x.AdvanceId });
        Constraint(line, "CK_CashAdvanceLine_Settlement",
            "[SettlementAmount] IS NULL OR [SettlementAmount]>=0");
    }
}
