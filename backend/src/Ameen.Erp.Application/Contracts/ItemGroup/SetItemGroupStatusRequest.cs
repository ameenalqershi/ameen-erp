using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetItemGroupStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
