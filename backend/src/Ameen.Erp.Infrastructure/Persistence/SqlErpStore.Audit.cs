using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class SqlErpStore
{
    public async Task<Page<AuditEvent>> ReadAuditPage(Guid companyId, AuditReadFilter filter, int offset, int limit, CancellationToken ct)
    {
        var query = db.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == companyId);
        if (filter.FromInclusive is DateTimeOffset from) query = query.Where(x => x.OccurredAt >= from);
        if (filter.BeforeExclusive is DateTimeOffset until) query = query.Where(x => x.OccurredAt < until);
        // OIDC subjects and persisted action keys are ordinal identifiers, independent of database collation.
        if (filter.ActorId is string actor)
            query = query.Where(x => EF.Functions.Collate(x.ActorId, "Latin1_General_100_BIN2") == actor && EF.Functions.DataLength(x.ActorId) == actor.Length * 2);
        if (filter.Action is string action)
            query = query.Where(x => EF.Functions.Collate(x.Action, "Latin1_General_100_BIN2") == action && EF.Functions.DataLength(x.Action) == action.Length * 2);
        if (filter.ResourceId is Guid resource) query = query.Where(x => x.ResourceId == resource);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
        return new(items, count, offset, limit);
    }
}
