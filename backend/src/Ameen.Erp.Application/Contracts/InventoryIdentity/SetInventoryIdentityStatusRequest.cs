namespace Ameen.Erp.Application;

public sealed record SetInventoryIdentityStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
