using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetBranchStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
