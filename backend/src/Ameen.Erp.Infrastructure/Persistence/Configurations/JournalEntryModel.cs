using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureJournalEntry(ModelBuilder model)
    {
        var journal = Tenant<JournalEntry>(model, "Journals");
        journal.Property(x => x.PreviousTrialBalanceReference).HasMaxLength(500);
        journal.Property(x => x.OpeningCurrencyCode).HasMaxLength(3);
        journal.Property(x => x.OpeningExchangeRate).HasPrecision(20, 10);
        Constraint(journal, "CK_Journal_GeneralOpening", "([Source] = 4 AND [OperationStartsOn] IS NOT NULL AND [PreviousTrialBalanceReference] IS NOT NULL AND LEN([PreviousTrialBalanceReference]) > 0 AND [OpeningCurrencyCode] IS NOT NULL AND [OpeningExchangeRate] > 0 AND [OpeningExchangeRate] < 1000000000 AND [OpeningExchangeRate] IS NOT NULL AND [OpeningRevision] > 0 AND [OpeningRevision] IS NOT NULL AND [OpeningModifiedAt] IS NOT NULL) OR ([Source] <> 4 AND [OperationStartsOn] IS NULL AND [PreviousTrialBalanceReference] IS NULL AND [OpeningCurrencyCode] IS NULL AND [OpeningExchangeRate] IS NULL AND [OpeningRevision] IS NULL AND [OpeningModifiedAt] IS NULL)");
        journal.Property(x => x.Number).HasMaxLength(64); journal.Property(x => x.Description).HasMaxLength(500);
        journal.Property(x => x.ExternalReference).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        journal.Property(x => x.CreatedBy).HasMaxLength(200);
        journal.Property(x => x.ApprovedBy).HasMaxLength(200); journal.Property(x => x.ReversalReason).HasMaxLength(500);
        journal.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        journal.HasIndex(x => new { x.CompanyId, x.BranchId, x.AccountingDate, x.State });
        journal.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.State });
        journal.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        journal.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId }).OnDelete(DeleteBehavior.NoAction);
        journal.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ReversesJournalId }).OnDelete(DeleteBehavior.NoAction);
        // ReversalJournalId and PendingReversalJournalId are guarded references in the transaction.
        // They deliberately are not cyclic FKs, so initial reversal creation has a valid insert order.
        journal.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.CompanyId, x.JournalId }).OnDelete(DeleteBehavior.Cascade);
        journal.Navigation(x => x.Lines).AutoInclude();
        Constraint(journal, "CK_Journal_State", "[State] BETWEEN 0 AND 5");
    }
}
