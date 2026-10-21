using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetWarehouseStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
