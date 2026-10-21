using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureExchangeRate(ModelBuilder model)
    {
        var fx = Tenant<ExchangeRate>(model, "ExchangeRates");
        fx.Property(x => x.CurrencyCode).HasMaxLength(3); fx.Property(x => x.Source).HasMaxLength(200);
        fx.Property(x => x.RateToFunctional).HasPrecision(20, 10);
        fx.HasIndex(x => new { x.CompanyId, x.CurrencyCode, x.EffectiveDate }).IsUnique();
        fx.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrencyCode })
            .HasPrincipalKey(x => new { x.CompanyId, x.Code }).OnDelete(DeleteBehavior.NoAction);
        Constraint(fx, "CK_ExchangeRate_Positive", "[RateToFunctional] > 0");
    }
}
