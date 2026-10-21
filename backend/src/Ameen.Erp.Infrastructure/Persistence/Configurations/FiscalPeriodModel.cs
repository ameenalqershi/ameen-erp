using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureFiscalPeriod(ModelBuilder model)
    {
        var period = Tenant<FiscalPeriod>(model, "FiscalPeriods");
        period.Property(x => x.Code).HasMaxLength(32); period.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        period.HasIndex(x => new { x.CompanyId, x.StartsOn, x.EndsOn });
        period.HasOne<FiscalYear>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalYearId }).OnDelete(DeleteBehavior.NoAction);
        Constraint(period, "CK_Period_Dates", "[EndsOn] >= [StartsOn]");
    }
}
