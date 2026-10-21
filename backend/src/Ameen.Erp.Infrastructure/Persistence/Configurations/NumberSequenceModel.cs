using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureNumberSequence(ModelBuilder model)
    {
        var seq = Tenant<NumberSequence>(model, "NumberSequences");
        seq.Property(x => x.Scope).HasMaxLength(80); seq.HasIndex(x => new { x.CompanyId, x.Scope }).IsUnique();
        Constraint(seq, "CK_Sequence_Positive", "[LastNumber] > 0");
    }
}
