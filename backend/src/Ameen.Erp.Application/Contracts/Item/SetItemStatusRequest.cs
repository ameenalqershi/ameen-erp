using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetItemStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
