using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureInventoryMovementReason(ModelBuilder model)
    {
        var reason = Tenant<InventoryMovementReason>(model, "InventoryMovementReasons");
        reason.Property(x => x.StatusCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        reason.Property(x => x.ReasonCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        reason.Property(x => x.DirectionCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        reason.Property(x => x.Description).HasMaxLength(200);
        reason.Property(x => x.CreatedBy).HasMaxLength(200);
        // Scope of code uniqueness and direction semantics remain unapproved.
        reason.HasIndex(x => new { x.CompanyId, x.ReasonCode });
        reason.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        reason.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CounterAccountId }).OnDelete(DeleteBehavior.NoAction);
        reason.HasOne<ApprovalRoute>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ApprovalRouteId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(reason, "CK_InventoryMovementReason_Audit",
            "[Revision] >= 1 AND [LastModifiedAt] >= [CreatedAt] AND LEN(LTRIM(RTRIM([CreatedBy]))) > 0");
        Constraint(reason, "CK_InventoryMovementReason_Fields",
            "LEN(LTRIM(RTRIM([StatusCode]))) > 0 AND LEN(LTRIM(RTRIM([ReasonCode]))) > 0 AND LEN(LTRIM(RTRIM([DirectionCode]))) > 0 AND LEN(LTRIM(RTRIM([Description]))) > 0");
        Constraint(reason, "CK_InventoryMovementReason_References",
            "[ApprovalRouteId] <> '00000000-0000-0000-0000-000000000000' AND ([BranchId] IS NULL OR [BranchId] <> '00000000-0000-0000-0000-000000000000') AND ([CounterAccountId] IS NULL OR [CounterAccountId] <> '00000000-0000-0000-0000-000000000000')");
    }
}
