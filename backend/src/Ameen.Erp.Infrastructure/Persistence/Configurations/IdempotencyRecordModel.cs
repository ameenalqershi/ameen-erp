using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureIdempotencyRecord(ModelBuilder model)
    {
        var idem = Tenant<IdempotencyRecord>(model, "IdempotencyRecords");
        idem.Property(x => x.Key).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        idem.Property(x => x.Operation).HasMaxLength(120).UseCollation("Latin1_General_100_BIN2");
        idem.Property(x => x.ActorId).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        idem.Property(x => x.Fingerprint).HasMaxLength(64);
        idem.HasIndex(x => new { x.CompanyId, x.Key }).IsUnique();
    }
}
