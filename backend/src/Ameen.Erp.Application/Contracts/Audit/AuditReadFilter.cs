namespace Ameen.Erp.Application;

public sealed record AuditReadFilter(DateTimeOffset? FromInclusive, DateTimeOffset? BeforeExclusive,
    string? ActorId, string? Action, Guid? ResourceId);
