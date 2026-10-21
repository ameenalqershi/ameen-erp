using System.Text.Json;
using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext : DbContext
{
    public const int SchemaVersion = 6;
    public ErpDbContext(DbContextOptions<ErpDbContext> options) : base(options) { }

    private static EntityTypeBuilder<T> Tenant<T>(ModelBuilder model, string table) where T : CompanyEntity
    {
        var b = model.Entity<T>(); b.HasBaseType((Type?)null); b.ToTable(table);
        b.HasKey(x => new { x.CompanyId, x.Id }); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        return b;
    }

    private static void Constraint<T>(EntityTypeBuilder<T> b, string name, string sql) where T : class
    {
        b.ToTable(t => t.HasCheckConstraint(name, sql));
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        var company = model.Entity<Company>(); company.HasBaseType((Type?)null); company.ToTable("Companies");
        company.HasKey(x => x.Id); company.Property(x => x.Id).ValueGeneratedNever();
        company.Property(x => x.Version).IsConcurrencyToken(); company.HasIndex(x => x.Code).IsUnique();
        company.Property(x => x.Code).HasMaxLength(32); company.Property(x => x.Name).HasMaxLength(200);
        company.Property(x => x.CountryCode).HasMaxLength(2); company.Property(x => x.FunctionalCurrency).HasMaxLength(3);
        Constraint(company, "CK_Company_Scale", "[CurrencyScale] BETWEEN 0 AND 6");
        company.Ignore(x => x.IsProfileComplete);
        company.Property(x => x.EnglishName).HasMaxLength(200); company.Property(x => x.Notes).HasMaxLength(2000);
        company.Property(x => x.LegalName).HasMaxLength(200); company.Property(x => x.Jurisdiction).HasMaxLength(200);
        company.Property(x => x.FiscalYearStartsOn).HasColumnType("date");
        company.Property(x => x.CommercialRegistration).HasMaxLength(64); company.Property(x => x.TaxRegistration).HasMaxLength(64);
        company.Property(x => x.City).HasMaxLength(200); company.Property(x => x.District).HasMaxLength(200);
        company.Property(x => x.Street).HasMaxLength(300); company.Property(x => x.BuildingNumber).HasMaxLength(32);
        company.Property(x => x.PostalCode).HasMaxLength(32); company.Property(x => x.AdditionalNumber).HasMaxLength(32);
        Constraint(company, "CK_Company_Revision", "[Revision] > 0");

        var currency = Tenant<Currency>(model, "Currencies");
        currency.Property(x => x.Code).HasMaxLength(3);
        currency.Property(x => x.Name).HasMaxLength(200); currency.Property(x => x.EnglishName).HasMaxLength(200);
        currency.Property(x => x.Notes).HasMaxLength(2000); currency.Property(x => x.DisplaySymbol).HasMaxLength(20);
        currency.Property(x => x.FractionName).HasMaxLength(100);
        Constraint(currency, "CK_Currency_Revision", "[Revision] > 0");
        currency.HasAlternateKey(x => new { x.CompanyId, x.Code });
        Constraint(currency, "CK_Currency_Scale", "[Scale] BETWEEN 0 AND 6");

        var branch = Tenant<Branch>(model, "Branches");
        branch.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        branch.Property(x => x.Code).HasMaxLength(32); branch.Property(x => x.Name).HasMaxLength(200);

        var member = Tenant<Membership>(model, "Memberships");
        member.Property(x => x.Subject).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        member.HasIndex(x => new { x.CompanyId, x.Subject }).IsUnique();
        member.Property(x => x.GrantedPermissions).HasConversion(
            x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
            x => JsonSerializer.Deserialize<string[]>(x, (JsonSerializerOptions?)null)!).Metadata.SetValueComparer(
                new ValueComparer<string[]>((a, b) => a!.SequenceEqual(b!),
                    x => x.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())), x => x.ToArray()));
        member.Property(x => x.BranchIds).HasConversion(
            x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
            x => JsonSerializer.Deserialize<Guid[]>(x, (JsonSerializerOptions?)null)!).Metadata.SetValueComparer(
                new ValueComparer<Guid[]>((a, b) => a!.SequenceEqual(b!),
                    x => x.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())), x => x.ToArray()));

        var fx = Tenant<ExchangeRate>(model, "ExchangeRates");
        fx.Property(x => x.CurrencyCode).HasMaxLength(3); fx.Property(x => x.Source).HasMaxLength(200);
        fx.Property(x => x.RateToFunctional).HasPrecision(20, 10);
        fx.HasIndex(x => new { x.CompanyId, x.CurrencyCode, x.EffectiveDate }).IsUnique();
        fx.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(fx, "CK_ExchangeRate_Positive", "[RateToFunctional] > 0");

        var year = Tenant<FiscalYear>(model, "FiscalYears");
        year.Property(x => x.Code).HasMaxLength(16); year.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        Constraint(year, "CK_FiscalYear_Dates", "[EndsOn] >= [StartsOn]");
        var period = Tenant<FiscalPeriod>(model, "FiscalPeriods");
        period.Property(x => x.Code).HasMaxLength(32); period.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        period.HasIndex(x => new { x.CompanyId, x.StartsOn, x.EndsOn });
        period.HasOne<FiscalYear>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalYearId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(period, "CK_Period_Dates", "[EndsOn] >= [StartsOn]");

        var center = Tenant<CostCenter>(model, "CostCenters");
        center.Property(x => x.Code).HasMaxLength(32); center.Property(x => x.Name).HasMaxLength(200);
        center.Property(x => x.EnglishName).HasMaxLength(200); center.Property(x => x.Notes).HasMaxLength(2000);
        center.HasOne<CostCenter>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId }).OnDelete(DeleteBehavior.NoAction);
        center.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ResponsibleMembershipId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(center, "CK_CostCenter_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
        Constraint(center, "CK_CostCenter_Revision", "[Revision] > 0");
        center.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        center.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId }).OnDelete(DeleteBehavior.NoAction);

        var account = Tenant<Account>(model, "Accounts");
        account.Property(x => x.Code).HasMaxLength(32); account.Property(x => x.Name).HasMaxLength(200);
        account.Property(x => x.EnglishName).HasMaxLength(200); account.Property(x => x.Notes).HasMaxLength(2000);
        account.Property(x => x.CurrencyCode).HasMaxLength(3);
        account.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        account.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ParentId }).OnDelete(DeleteBehavior.NoAction);
        account.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(account, "CK_Account_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
        Constraint(account, "CK_Account_NormalBalance", "[NormalBalance] BETWEEN 0 AND 1");
        Constraint(account, "CK_Account_Revision", "[Revision] > 0");

        var journal = Tenant<JournalEntry>(model, "Journals");
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

        var line = Tenant<JournalLine>(model, "JournalLines");
        line.Property(x => x.Description).HasMaxLength(500); line.Property(x => x.CurrencyCode).HasMaxLength(3);
        line.Property(x => x.Debit).HasPrecision(20, 6); line.Property(x => x.Credit).HasPrecision(20, 6);
        line.Property(x => x.FunctionalDebit).HasPrecision(20, 6); line.Property(x => x.FunctionalCredit).HasPrecision(20, 6);
        line.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        line.HasIndex(x => new { x.CompanyId, x.JournalId, x.Position }).IsUnique();
        line.HasIndex(x => new { x.CompanyId, x.AccountId, x.JournalId });
        line.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccountId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<CostCenter>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostCenterId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<ExchangeRate>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExchangeRateId }).OnDelete(DeleteBehavior.NoAction);
        line.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(line, "CK_Line_Amounts", "([Debit] > 0 AND [Credit] = 0 AND [FunctionalDebit] > 0 AND [FunctionalCredit] = 0) OR ([Credit] > 0 AND [Debit] = 0 AND [FunctionalCredit] > 0 AND [FunctionalDebit] = 0)");
        Constraint(line, "CK_Line_Rate", "[ExchangeRate] > 0");

        var seq = Tenant<NumberSequence>(model, "NumberSequences");
        seq.Property(x => x.Scope).HasMaxLength(80); seq.HasIndex(x => new { x.CompanyId, x.Scope }).IsUnique();
        Constraint(seq, "CK_Sequence_Positive", "[LastNumber] > 0");
        var audit = Tenant<AuditEvent>(model, "AuditEvents");
        audit.Property(x => x.ActorId).HasMaxLength(200); audit.Property(x => x.Action).HasMaxLength(100);
        audit.HasIndex(x => new { x.CompanyId, x.OccurredAt, x.ResourceId });
        var outbox = Tenant<OutboxEvent>(model, "OutboxEvents");
        outbox.Property(x => x.Type).HasMaxLength(100);
        outbox.HasIndex(x => new { x.CompanyId, x.ResourceId, x.Type }).IsUnique();
        outbox.HasIndex(x => new { x.DeliveredAt, x.OccurredAt });
        var idem = Tenant<IdempotencyRecord>(model, "IdempotencyRecords");
        idem.Property(x => x.Key).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        idem.Property(x => x.Operation).HasMaxLength(120).UseCollation("Latin1_General_100_BIN2");
        idem.Property(x => x.ActorId).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        idem.Property(x => x.Fingerprint).HasMaxLength(64);
        idem.HasIndex(x => new { x.CompanyId, x.Key }).IsUnique();

        ConfigureRecurring(model);
        // Use one date-only storage contract for all accounting dates.
        foreach (var entityType in model.Model.GetEntityTypes().ToArray())
        foreach (var property in entityType.ClrType.GetProperties().Where(p => p.PropertyType == typeof(DateOnly)))
        {
            model.Entity(entityType.ClrType).Property<DateOnly>(property.Name).HasConversion(new ValueConverter<DateOnly, DateTime>(
                d => d.ToDateTime(TimeOnly.MinValue), d => DateOnly.FromDateTime(d))).HasColumnType("date");
        }
    }

    private void ValidateChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "audit_immutable", "سجل التدقيق غير قابل للتعديل أو الحذف.");
        foreach (var entry in ChangeTracker.Entries<ExchangeRate>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "exchange_rate_immutable", "سعر الصرف التاريخي غير قابل للتعديل أو الحذف.");
        foreach (var entry in ChangeTracker.Entries<IdempotencyRecord>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "receipt_immutable", "نتيجة الطلب المحفوظة غير قابلة للتعديل.");
        foreach (var entry in ChangeTracker.Entries<RecurringJournalGeneration>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "recurring_generation_immutable", "سجل توليد القيد غير قابل للتعديل أو الحذف.");
        foreach (var entry in ChangeTracker.Entries<RecurringJournalLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            Check.That(ChangeTracker.Entries<RecurringJournalTemplate>().Any(t => t.Entity.CompanyId == entry.Entity.CompanyId && t.Entity.Id == entry.Entity.TemplateId &&
                (t.State == EntityState.Added || (t.State == EntityState.Modified && t.Property(x => x.Version).IsModified))),
                "recurring_lines_owner_required", "تعديل السطور يتطلب تحميل القالب ضمن المعاملة.");
        foreach (var entry in ChangeTracker.Entries<JournalLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var owner = ChangeTracker.Entries<JournalEntry>().SingleOrDefault(e => e.Entity.CompanyId == entry.Entity.CompanyId && e.Entity.Id == entry.Entity.JournalId);
            Check.That(owner is not null && owner.Entity.State == JournalState.Draft &&
                (owner.State == EntityState.Added || owner.OriginalValues.GetValue<JournalState>(nameof(JournalEntry.State)) == JournalState.Draft),
                "journal_lines_immutable", "تعديل السطور مسموح داخل مسودة محملة فقط.");
        }
        foreach (var entry in ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            var sourceFields = new[] { nameof(JournalEntry.Source), nameof(JournalEntry.RecurringTemplateId), nameof(JournalEntry.SourceTemplateVersion), nameof(JournalEntry.ReversesJournalId) };
            Check.That(!entry.Properties.Any(p => p.IsModified && sourceFields.Contains(p.Metadata.Name)),
                "journal_source_immutable", "لا يمكن تغيير مصدر قيد محفوظ.");
            var previous = entry.OriginalValues.GetValue<JournalState>(nameof(JournalEntry.State));
            if (previous is not (JournalState.Posted or JournalState.Reversed)) continue;
            Check.That(entry.State != EntityState.Deleted, "posted_journal_immutable", "لا يمكن حذف قيد مرحل.");
            var allowed = new[] { nameof(JournalEntry.State), nameof(JournalEntry.ReversalJournalId), nameof(JournalEntry.PendingReversalJournalId), nameof(JournalEntry.Version) };
            Check.That(!entry.Properties.Any(p => p.IsModified && !allowed.Contains(p.Metadata.Name)),
                "posted_journal_immutable", "لا يمكن تعديل المحتوى المالي لقيد مرحل.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
