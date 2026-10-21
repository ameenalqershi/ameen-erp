namespace Ameen.Erp.Domain;

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
