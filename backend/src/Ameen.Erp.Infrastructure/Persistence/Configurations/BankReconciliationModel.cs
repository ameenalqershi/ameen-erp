using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureBankReconciliations(ModelBuilder model)
    {
        var header = Tenant<BankReconciliation>(model, "BankReconciliations");
        header.Property(x => x.State).HasMaxLength(32);
        header.Property(x => x.OpeningBalance).HasPrecision(20, 6);
        header.Property(x => x.ClosingBalance).HasPrecision(20, 6);
        header.Property(x => x.StatementFileId).HasMaxLength(300);
        header.Property(x => x.CreatedBy).HasMaxLength(200);
        header.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        header.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        header.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        header.HasOne<BankAccount>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        header.HasIndex(x => new { x.CompanyId, x.BankAccountId,
            x.StatementFrom, x.StatementTo });
        Constraint(header, "CK_BankReconciliation_Dates", "[StatementFrom]<=[StatementTo]");
        Constraint(header, "CK_BankReconciliation_Audit", "[Revision]>0 AND [CreatedAt]<=[LastModifiedAt]");
        Constraint(header, "CK_BankReconciliation_State", "LEN(LTRIM(RTRIM([State])))>0");

        var line = Tenant<BankReconciliationLine>(model, "BankReconciliationLines");
        line.Property(x => x.MatchedAmount).HasPrecision(20, 6);
        line.Property(x => x.VarianceReason).HasMaxLength(1000);
        line.HasOne<BankReconciliation>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReconciliationId })
            .OnDelete(DeleteBehavior.NoAction);
        line.HasIndex(x => new { x.CompanyId, x.ReconciliationId, x.BankMovementId });
        Constraint(line, "CK_BankReconciliationLine_Amount", "[MatchedAmount]>=0");
    }
}
