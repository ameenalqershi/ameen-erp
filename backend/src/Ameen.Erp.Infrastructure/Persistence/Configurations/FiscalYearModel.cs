using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureFiscalYear(ModelBuilder model)
    {
        var year = Tenant<FiscalYear>(model, "FiscalYears");
        year.Property(x => x.Code).HasMaxLength(16); year.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        year.Property(x => x.CreatedBy).HasMaxLength(200);
        year.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        year.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        Constraint(year, "CK_FiscalYear_Dates", "[EndsOn] >= [StartsOn]");
        Constraint(year, "CK_FiscalYear_AuditMetadata",
            "([CreatedBy] IS NULL AND [CreatedAt] IS NULL AND [Revision] IS NULL AND [LastModifiedAt] IS NULL)" +
            " OR ([CreatedBy] IS NOT NULL AND LEN(LTRIM(RTRIM([CreatedBy]))) > 0" +
            " AND [CreatedAt] IS NOT NULL AND [Revision] >= 1 AND [LastModifiedAt] >= [CreatedAt])");
    }
}
