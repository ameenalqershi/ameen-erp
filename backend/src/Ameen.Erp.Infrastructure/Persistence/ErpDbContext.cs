using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext : DbContext
{
    public const int SchemaVersion = 19;
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
        ConfigureCompany(model);
        ConfigureCurrency(model);
        ConfigureItem(model);
        ConfigureItemUnit(model);
        ConfigureItemGroup(model);
        ConfigureUnitOfMeasure(model);
        ConfigureBranch(model);
        ConfigureMembership(model);
        ConfigureExchangeRate(model);
        ConfigureFiscalYear(model);
        ConfigureFiscalPeriod(model);
        ConfigureCostCenter(model);
        ConfigureAccount(model);
        ConfigureJournalEntry(model);
        ConfigureJournalLine(model);
        ConfigureNumberSequence(model);
        ConfigureAuditEvent(model);
        ConfigureOutboxEvent(model);
        ConfigureIdempotencyRecord(model);
        ConfigureWarehouses(model);
        ConfigureInventoryIdentity(model);
        ConfigureInventoryOpening(model);
        ConfigureInventoryPosting(model);
        ConfigureRecurring(model);
        // Use one date-only storage contract for all accounting dates.
        foreach (var entityType in model.Model.GetEntityTypes().ToArray())
        foreach (var property in entityType.ClrType.GetProperties().Where(p => p.PropertyType == typeof(DateOnly)))
        {
            model.Entity(entityType.ClrType).Property<DateOnly>(property.Name).HasConversion(new ValueConverter<DateOnly, DateTime>(
                d => d.ToDateTime(TimeOnly.MinValue), d => DateOnly.FromDateTime(d))).HasColumnType("date");
        }
    }

}
