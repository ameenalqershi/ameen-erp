namespace Ameen.Erp.Domain;

public sealed class OutboxEvent : CompanyEntity
{
    public string Type { get; private set; } = "";
    public Guid ResourceId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Payload { get; private set; } = "";
    public DateTimeOffset? DeliveredAt { get; private set; }
    private OutboxEvent() { }
    public OutboxEvent(Guid companyId, string type, Guid resourceId, DateTimeOffset now, string payload) : base(companyId)
    {
        Type = type; ResourceId = resourceId; OccurredAt = now; Payload = payload;
    }
}
