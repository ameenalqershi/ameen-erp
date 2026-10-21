using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureAuditEvent(ModelBuilder model)
    {
        var audit = Tenant<AuditEvent>(model, "AuditEvents");
        audit.Property(x => x.ActorId).HasMaxLength(200); audit.Property(x => x.Action).HasMaxLength(100);
        audit.HasIndex(x => new { x.CompanyId, x.OccurredAt, x.ResourceId });
    }
}
