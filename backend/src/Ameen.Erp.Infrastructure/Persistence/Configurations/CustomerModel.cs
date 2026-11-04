using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCustomer(ModelBuilder model)
    {
        var customer = Tenant<Customer>(model, "Customers");
        customer.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        customer.Property(x => x.Name).HasMaxLength(200);
        customer.Property(x => x.EnglishName).HasMaxLength(200);
        customer.Property(x => x.LegalName).HasMaxLength(200);
        customer.Property(x => x.Notes).HasMaxLength(2000);
        customer.Property(x => x.Phone).HasMaxLength(100);
        customer.Property(x => x.IdentityType).HasMaxLength(32);
        customer.Property(x => x.TaxNumber).HasMaxLength(64);
        customer.Property(x => x.City).HasMaxLength(200);
        customer.Property(x => x.District).HasMaxLength(200);
        customer.Property(x => x.Street).HasMaxLength(200);
        customer.Property(x => x.BuildingNumber).HasMaxLength(32);
        customer.Property(x => x.PostalCode).HasMaxLength(32);
        customer.Property(x => x.AdditionalNumber).HasMaxLength(32);
        customer.Property(x => x.CreatedBy).HasMaxLength(200);
        customer.Property(x => x.CreditLimit).HasPrecision(20, 6);
        customer.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        customer.HasOne<Account>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ControlAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        customer.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TransactionCurrencyId })
            .OnDelete(DeleteBehavior.NoAction);
        // SAL15 category, SAL09 salesperson and country reference catalogues do not
        // exist yet; no fictional FK or unapproved write workflow is enabled.
        Constraint(customer, "CK_Customer_Audit", "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");
        Constraint(customer, "CK_Customer_Credit", "[CreditLimit] >= 0 AND [CreditDays] >= 0");
    }
}
