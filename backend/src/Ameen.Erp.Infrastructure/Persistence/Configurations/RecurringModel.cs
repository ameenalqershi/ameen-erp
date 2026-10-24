using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureRecurring(ModelBuilder model)
    {
        var template = Tenant<RecurringJournalTemplate>(model, "RecurringJournalTemplates");
        template.Property(x => x.Name).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        template.Property(x => x.CreatedBy).HasMaxLength(200);
        template.HasIndex(x => new { x.CompanyId, x.BranchId, x.Name }).IsUnique();
        template.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);
        template.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.CompanyId, x.TemplateId }).OnDelete(DeleteBehavior.Cascade);
        template.Navigation(x => x.Lines).AutoInclude();
        Constraint(template, "CK_Recurring_Dates", "[EndsOn] >= [StartsOn]");
        Constraint(template, "CK_Recurring_Frequency", "[Frequency] BETWEEN 0 AND 2");
        Constraint(template, "CK_Recurring_Mode", "[GenerationMode] = 0");

        var line = Tenant<RecurringJournalLine>(model, "RecurringJournalLines");
        line.Property(x => x.Description).HasMaxLength(500); line.Property(x => x.CurrencyCode).HasMaxLength(3);
        line.Property(x => x.Debit).HasPrecision(20, 6); line.Property(x => x.Credit).HasPrecision(20, 6);
        line.HasIndex(x => new { x.CompanyId, x.TemplateId, x.Position }).IsUnique();
        line.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccountId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<CostCenter>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostCenterId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(line, "CK_RecurringLine_Amounts", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)");
        Constraint(line, "CK_RecurringLine_Position", "[Position] BETWEEN 1 AND 200");

        var journal = model.Entity<JournalEntry>();
        journal.Property(x => x.Source).HasDefaultValue(JournalSource.Manual);
        journal.HasOne<RecurringJournalTemplate>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RecurringTemplateId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(journal, "CK_Journal_Source",
            "([Source] = 0 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL) OR " +
            "([Source] = 1 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NOT NULL AND [SourceTemplateVersion] IS NOT NULL AND [ReversesJournalId] IS NULL) OR " +
            "([Source] = 2 AND [InventoryOpeningId] IS NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NOT NULL) OR " +
            "([Source] = 3 AND [InventoryOpeningId] IS NOT NULL AND [RecurringTemplateId] IS NULL AND [SourceTemplateVersion] IS NULL AND [ReversesJournalId] IS NULL)");

        var generation = Tenant<RecurringJournalGeneration>(model, "RecurringJournalGenerations");
        generation.Property(x => x.GeneratedBy).HasMaxLength(200);
        generation.HasIndex(x => new { x.CompanyId, x.TemplateId, x.FiscalPeriodId }).IsUnique();
        generation.HasIndex(x => new { x.CompanyId, x.JournalId }).IsUnique();
        generation.HasOne<RecurringJournalTemplate>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TemplateId }).OnDelete(DeleteBehavior.NoAction);
        generation.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId }).OnDelete(DeleteBehavior.NoAction);
        generation.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.CompanyId, x.JournalId }).OnDelete(DeleteBehavior.NoAction);
    }
}
