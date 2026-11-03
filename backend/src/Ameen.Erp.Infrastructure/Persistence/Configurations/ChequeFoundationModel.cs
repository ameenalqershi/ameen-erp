using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureChequeFoundations(ModelBuilder model)
    {
        var received = Tenant<IncomingCheque>(model, "IncomingCheques");
        ConfigureChequeCommon(received);
        received.Property(x => x.DrawnBankName).HasMaxLength(200);
        received.HasOne<Account>().WithMany().HasForeignKey(x =>
            new { x.CompanyId, x.CollectionAccountId }).OnDelete(DeleteBehavior.NoAction);
        received.HasIndex(x => new { x.CompanyId, x.DrawnBankName, x.PayerId, x.ChequeNumber });
        // CASH07 uniqueness is bank + payer "according to policy"; scope and
        // reuse rules are not approved, so this is NOT a unique index.

        var issued = Tenant<OutgoingCheque>(model, "OutgoingCheques");
        ConfigureChequeCommon(issued);
        issued.Property(x => x.ChequeState).HasMaxLength(32);
        issued.HasOne<BankAccount>().WithMany().HasForeignKey(x =>
            new { x.CompanyId, x.BankAccountId }).OnDelete(DeleteBehavior.NoAction);
        issued.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.ChequeNumber });
        Constraint(issued, "CK_OutgoingCheque_State",
            "LEN(LTRIM(RTRIM([ChequeState])))>0");
        // A cancelled cheque number is not reusable; actual number/cheque-book
        // allocation and transition policies remain explicitly unimplemented.
    }

    private static void ConfigureChequeCommon<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> doc)
        where T : CompanyEntity
    {
        // In this project the incoming and outgoing documents have the same
        // property names but deliberately independent physical tables.
        // Property definitions are applied by EF metadata for both models.
        var entity = doc.Metadata;
        foreach (var name in new[] { "Number", "ChequeNumber" })
            doc.Property<string>(name).HasMaxLength(64);
        foreach (var name in new[] { "Description" })
            doc.Property<string>(name).HasMaxLength(1000);
        doc.Property<string>("ExternalReference").HasMaxLength(100);
        doc.Property<string>("State").HasMaxLength(32);
        doc.Property<string>("CurrencyCode").HasMaxLength(3);
        doc.Property<decimal>("ExchangeRate").HasPrecision(20, 10);
        doc.Property<decimal>("Amount").HasPrecision(20, 6);
        doc.Property<string>("CreatedBy").HasMaxLength(200);
        doc.Property<DateTimeOffset>("CreatedAt").HasColumnType("datetimeoffset(7)");
        doc.Property<DateTimeOffset>("LastModifiedAt").HasColumnType("datetimeoffset(7)");
        doc.HasOne<Branch>().WithMany().HasForeignKey("CompanyId", "BranchId")
            .OnDelete(DeleteBehavior.NoAction);
        doc.HasOne<FiscalYear>().WithMany().HasForeignKey("CompanyId", "FiscalYearId")
            .OnDelete(DeleteBehavior.NoAction);
        doc.HasOne<Currency>().WithMany().HasForeignKey("CompanyId", "CurrencyCode")
            .HasPrincipalKey("CompanyId", "Code").OnDelete(DeleteBehavior.NoAction);
        var namePrefix = typeof(T) == typeof(IncomingCheque) ? "IncomingCheque" : "OutgoingCheque";
        Constraint(doc, "CK_" + namePrefix + "_Revision", "[Revision]>0");
        Constraint(doc, "CK_" + namePrefix + "_AuditTime", "[CreatedAt]<=[LastModifiedAt]");
        Constraint(doc, "CK_" + namePrefix + "_Rate", "[ExchangeRate]>0");
        Constraint(doc, "CK_" + namePrefix + "_Amount", "[Amount]>0");
        Constraint(doc, "CK_" + namePrefix + "_Text",
            "LEN(LTRIM(RTRIM([State])))>0 AND LEN(LTRIM(RTRIM([ChequeNumber])))>0");
    }
}
