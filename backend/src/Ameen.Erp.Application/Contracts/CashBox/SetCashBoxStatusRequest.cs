namespace Ameen.Erp.Application;

public sealed record SetCashBoxStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
