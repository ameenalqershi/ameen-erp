using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Infrastructure;
public sealed partial class ErpDbContext
{
    private static void ConfigureInventoryPosting(ModelBuilder model)
    {
        var settings = Tenant<InventoryOpeningPostingSettings>(model, "InventoryOpeningPostingSettings");
        settings.HasIndex(x => x.CompanyId).IsUnique();
        settings.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ClearingAccountId }).OnDelete(DeleteBehavior.NoAction);
        var movement = Tenant<StockMovement>(model, "StockMovements");
        movement.Property(x => x.CurrencyCode).HasMaxLength(3);
        movement.Property(x => x.BaseQuantity).HasPrecision(20, 6); movement.Property(x => x.Value).HasPrecision(20, 6);
        movement.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.ItemId, x.IdentityId }).IsUnique().HasFilter("[ReversesMovementId] IS NULL");
        movement.HasIndex(x => new { x.CompanyId, x.IdentityId }).IsUnique().HasFilter("[IsSerial] = 1 AND [ReversesMovementId] IS NULL");
        movement.HasIndex(x => new { x.CompanyId, x.OpeningId, x.ItemId, x.IdentityId }).IsUnique().HasFilter(null);
        movement.HasIndex(x => new { x.CompanyId, x.BranchId, x.AccountingDate });
        movement.HasIndex(x => new { x.CompanyId, x.ReversesMovementId }).IsUnique().HasFilter("[ReversesMovementId] IS NOT NULL");
        movement.HasOne<StockMovement>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ReversesMovementId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<InventoryOpening>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OpeningId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.CompanyId, x.WarehouseId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<Item>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ItemId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<InventoryIdentity>().WithMany().HasForeignKey(x => new { x.CompanyId, x.IdentityId }).OnDelete(DeleteBehavior.NoAction);
        movement.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InventoryAccountId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(movement, "CK_StockMovement_Amounts", "([ReversesMovementId] IS NULL AND [BaseQuantity] > 0 AND [Value] >= 0) OR ([ReversesMovementId] IS NOT NULL AND [BaseQuantity] < 0 AND [Value] <= 0)");
        Constraint(movement, "CK_StockMovement_Serial", "[IsSerial] = 0 OR ([IdentityId] IS NOT NULL AND ABS([BaseQuantity]) = 1)");
        var opening = model.Entity<InventoryOpening>();
        opening.Property(x => x.ReversalReason).HasMaxLength(500);
        opening.HasOne<InventoryOpening>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ReversesOpeningId }).OnDelete(DeleteBehavior.NoAction);
        opening.HasIndex(x => new { x.CompanyId, x.ReversesOpeningId }).IsUnique().HasFilter("[ReversesOpeningId] IS NOT NULL AND [State] <> 4");
        Constraint(opening, "CK_InventoryOpening_Reversal", "([ReversesOpeningId] IS NULL AND [ReversalReason] IS NULL) OR ([ReversesOpeningId] IS NOT NULL AND [ReversesOpeningId] <> [Id] AND [ReversalReason] IS NOT NULL)");
        opening.Property(x => x.PostedBy).HasMaxLength(200);
        opening.HasIndex(x => new { x.CompanyId, x.JournalId }).IsUnique().HasFilter("[JournalId] IS NOT NULL");
        opening.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InventoryAccountId }).OnDelete(DeleteBehavior.NoAction);
        opening.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ClearingAccountId }).OnDelete(DeleteBehavior.NoAction);
        // Journal owns the source FK; reverse pointer is validated with the tracked posting aggregate, avoiding cyclic insertion dependencies.
        Constraint(opening, "CK_InventoryOpening_Posted", "([State] = 5 AND [JournalId] IS NOT NULL AND [InventoryAccountId] IS NOT NULL AND [ClearingAccountId] IS NOT NULL AND [PostingSettingsVersion] IS NOT NULL AND [PostedBy] IS NOT NULL AND [PostedAt] IS NOT NULL) OR ([State] <> 5 AND [JournalId] IS NULL AND [InventoryAccountId] IS NULL AND [ClearingAccountId] IS NULL AND [PostingSettingsVersion] IS NULL AND [PostedBy] IS NULL AND [PostedAt] IS NULL)");
        var journal = model.Entity<JournalEntry>();
        journal.HasOne<InventoryOpening>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InventoryOpeningId }).OnDelete(DeleteBehavior.NoAction);
        journal.HasIndex(x => new { x.CompanyId, x.InventoryOpeningId }).IsUnique().HasFilter("[InventoryOpeningId] IS NOT NULL");
    }
}
