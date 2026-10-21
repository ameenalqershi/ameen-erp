namespace Ameen.Erp.Domain;

public sealed class AuditEvent : CompanyEntity
{
    public string ActorId { get; private set; } = "";
    public string Action { get; private set; } = "";
    public Guid ResourceId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Details { get; private set; } = "";
    private AuditEvent() { }
    public AuditEvent(Guid companyId, string actor, string action, Guid resourceId, DateTimeOffset now, string details) : base(companyId)
    {
        ActorId = actor; Action = action; ResourceId = resourceId; OccurredAt = now; Details = details;
    }
}
