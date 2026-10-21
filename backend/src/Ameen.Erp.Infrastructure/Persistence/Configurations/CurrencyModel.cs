using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureCurrency(ModelBuilder model)
    {
        var currency = Tenant<Currency>(model, "Currencies");
        currency.Property(x => x.Code).HasMaxLength(3);
        currency.Property(x => x.Name).HasMaxLength(200); currency.Property(x => x.EnglishName).HasMaxLength(200);
        currency.Property(x => x.Notes).HasMaxLength(2000); currency.Property(x => x.DisplaySymbol).HasMaxLength(20);
        currency.Property(x => x.FractionName).HasMaxLength(100);
        Constraint(currency, "CK_Currency_Revision", "[Revision] > 0");
        currency.HasAlternateKey(x => new { x.CompanyId, x.Code });
        Constraint(currency, "CK_Currency_Scale", "[Scale] BETWEEN 0 AND 6");
    }
}
