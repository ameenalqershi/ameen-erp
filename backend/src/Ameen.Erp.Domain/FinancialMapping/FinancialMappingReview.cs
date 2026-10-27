namespace Ameen.Erp.Domain;
public sealed class FinancialMappingReview : CompanyEntity
{
    public Guid MappingId { get; private set; }
    public Guid MappingVersion { get; private set; }
    public DateOnly AsOf { get; private set; }
    public string ReviewedBy { get; private set; } = "";
    public DateTimeOffset ReviewedAt { get; private set; }
    public bool Ready { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    private FinancialMappingReview() { }
    public FinancialMappingReview(FinancialMapping map,DateOnly asOf,string actor,DateTimeOffset now,bool ready,string snapshot):base(map.CompanyId)
    {MappingId=map.Id;MappingVersion=map.Version;AsOf=asOf;ReviewedBy=actor;ReviewedAt=now;Ready=ready;SnapshotJson=snapshot;}
}
