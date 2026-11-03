using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureChequeMovements(ModelBuilder model)
    {
        var movement = Tenant<ChequeMovement>(model, "ChequeMovements");
        movement.Ignore(x => x.ChequeId); // computed original CASH09-F04
        movement.Property(x => x.State).HasMaxLength(32);
        movement.Property(x => x.Action).HasMaxLength(32);
        movement.Property(x => x.RejectionReason).HasMaxLength(1000);
        movement.Property(x => x.Fee).HasPrecision(20, 6);
        movement.Property(x => x.CreatedBy).HasMaxLength(200);
        movement.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        movement.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        movement.HasOne<Branch>().WithMany().HasForeignKey(x =>
            new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<IncomingCheque>().WithMany().HasForeignKey(x =>
            new { x.CompanyId, x.IncomingChequeId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<OutgoingCheque>().WithMany().HasForeignKey(x =>
            new { x.CompanyId, x.OutgoingChequeId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<BankAccount>().WithMany().HasForeignKey(x =>
            new { x.CompanyId, x.ReceivedBankAccountId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasIndex(x => new { x.CompanyId, x.IncomingChequeId });
        movement.HasIndex(x => new { x.CompanyId, x.OutgoingChequeId });
        Constraint(movement, "CK_ChequeMovement_ExactlyOne",
            "([IncomingChequeId] IS NOT NULL AND [OutgoingChequeId] IS NULL) OR " +
            "([IncomingChequeId] IS NULL AND [OutgoingChequeId] IS NOT NULL)");
        Constraint(movement, "CK_ChequeMovement_Revision", "[Revision]>0");
        Constraint(movement, "CK_ChequeMovement_AuditTime", "[CreatedAt]<=[LastModifiedAt]");
        Constraint(movement, "CK_ChequeMovement_Fee", "[Fee] IS NULL OR [Fee]>=0");
        Constraint(movement, "CK_ChequeMovement_Text",
            "LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([Action])))>0");
    }
}
