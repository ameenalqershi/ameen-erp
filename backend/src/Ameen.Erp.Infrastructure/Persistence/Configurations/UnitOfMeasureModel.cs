using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureUnitOfMeasure(ModelBuilder model)
    {
        var unit = Tenant<UnitOfMeasure>(model, "UnitsOfMeasure");
        unit.Property(x => x.Code).HasMaxLength(32); unit.Property(x => x.Name).HasMaxLength(200);
        unit.Property(x => x.StandardCode).HasMaxLength(32); unit.Property(x => x.Description).HasMaxLength(500);
        unit.Property(x => x.EnglishName).HasMaxLength(200); unit.Property(x => x.Notes).HasMaxLength(2000);
        unit.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        Constraint(unit, "CK_Unit_QuantityScale", "[QuantityScale] BETWEEN 0 AND 6");
        Constraint(unit, "CK_Unit_Revision", "[Revision] > 0");
    }
}
