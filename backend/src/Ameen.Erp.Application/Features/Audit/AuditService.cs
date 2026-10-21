using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class AuditService : ApplicationService
{
    public AuditService(IErpStore store, ITransactionRunner transactions, IClock clock) : base(store, transactions, clock)
    {
    }


    public async Task<Page<AuditEvent>> ListAudit(Guid cid, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        return await store.ReadPage<AuditEvent>(cid, x => true, q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
}
