using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCompany(ModelBuilder model)
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
    }
}
