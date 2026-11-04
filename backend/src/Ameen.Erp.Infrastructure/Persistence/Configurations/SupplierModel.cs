using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureSupplier(ModelBuilder model)
    {
        var supplier = Tenant<Supplier>(model, "Suppliers");
        supplier.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        supplier.Property(x => x.Name).HasMaxLength(200);
        supplier.Property(x => x.EnglishName).HasMaxLength(200);
        supplier.Property(x => x.Notes).HasMaxLength(2000);
        supplier.Property(x => x.LegalName).HasMaxLength(200);
        supplier.Property(x => x.PaymentTerms).HasMaxLength(2000);
        supplier.Property(x => x.TaxNumber).HasMaxLength(64);
        supplier.Property(x => x.BankBeneficiary).HasMaxLength(200);
        supplier.Property(x => x.Iban).HasMaxLength(64);
        supplier.Property(x => x.CreatedBy).HasMaxLength(200);
        supplier.Property(x => x.City).HasMaxLength(200);
        supplier.Property(x => x.District).HasMaxLength(200);
        supplier.Property(x => x.Street).HasMaxLength(200);
        supplier.Property(x => x.BuildingNumber).HasMaxLength(32);
        supplier.Property(x => x.PostalCode).HasMaxLength(32);
        supplier.Property(x => x.AdditionalNumber).HasMaxLength(32);
        supplier.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        supplier.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ControlAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        supplier.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.TransactionCurrencyId })
            .OnDelete(DeleteBehavior.NoAction);
        // No approved country catalogue: do not invent a foreign key.
        Constraint(supplier, "CK_Supplier_Audit", "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");
        Constraint(supplier, "CK_Supplier_DueDays", "[DueDays] >= 0");
        Constraint(supplier, "CK_Supplier_Country", "[CountryId] <> '00000000-0000-0000-0000-000000000000'");
    }
}
