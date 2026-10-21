using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class AuditService : ApplicationService
{
    public AuditService(IErpStore store, ITransactionRunner transactions, IClock clock) : base(store, transactions, clock)
    {
    }


    public async Task<Page<AuditEvent>> ListAudit(Guid cid, Actor actor, int offset, int limit, CancellationToken ct,
        DateOnly? from = null, DateOnly? to = null, string? actorId = null, string? action = null, Guid? resourceId = null)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        Check.That(from is null || to is null || from <= to, "invalid_date_range", "نهاية الفترة قبل بدايتها.");
        actorId = actorId is null ? null : Check.Text(actorId, "هوية المستخدم", 200);
        action = action is null ? null : Check.Text(action, "نوع الحدث", 100);
        Check.That(resourceId != Guid.Empty, "invalid_audit_resource", "معرف المرجع غير صالح.");
        DateTimeOffset? start = from is DateOnly startDate ? new(startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
        DateTimeOffset? end = to is DateOnly endDate && endDate < DateOnly.MaxValue
            ? new(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
        return await store.ReadAuditPage(cid, new(start, end, actorId, action, resourceId), offset, limit, ct);
    }
    public async Task<AuditEvent> GetAuditEvent(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        return await Required<AuditEvent>(cid, id, ct);
    }
}
