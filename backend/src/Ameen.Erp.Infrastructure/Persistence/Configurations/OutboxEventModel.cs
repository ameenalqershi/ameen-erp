using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureOutboxEvent(ModelBuilder model)
    {
        var outbox = Tenant<OutboxEvent>(model, "OutboxEvents");
        outbox.Property(x => x.Type).HasMaxLength(100);
        outbox.HasIndex(x => new { x.CompanyId, x.ResourceId, x.Type }).IsUnique();
        outbox.HasIndex(x => new { x.DeliveredAt, x.OccurredAt });
    }
}
