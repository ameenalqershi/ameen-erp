using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetCostCenterStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
