using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureFiscalYear(ModelBuilder model)
    {
        var year = Tenant<FiscalYear>(model, "FiscalYears");
        year.Property(x => x.Code).HasMaxLength(16); year.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        Constraint(year, "CK_FiscalYear_Dates", "[EndsOn] >= [StartsOn]");
    }
}
