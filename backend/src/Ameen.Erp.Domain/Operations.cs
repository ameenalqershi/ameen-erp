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

public sealed class NumberSequence : CompanyEntity
{
    public string Scope { get; private set; } = "";
    public long LastNumber { get; private set; }
    private NumberSequence() { }
    public NumberSequence(Guid companyId, string scope) : base(companyId) { Scope = scope; }
    public long Next() { LastNumber = checked(LastNumber + 1); Touch(); return LastNumber; }
}

public sealed class IdempotencyRecord : CompanyEntity
{
    public string Key { get; private set; } = "";
    public string Operation { get; private set; } = "";
    public string ActorId { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public string ReceiptJson { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    private IdempotencyRecord() { }
    public IdempotencyRecord(Guid companyId, string key, string operation, string actor, string fingerprint, string receipt, DateTimeOffset now) : base(companyId)
    {
        Key = key; Operation = operation; ActorId = actor; Fingerprint = fingerprint;
        ReceiptJson = receipt; CreatedAt = now;
    }
}
