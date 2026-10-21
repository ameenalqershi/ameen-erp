using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetUnitStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
