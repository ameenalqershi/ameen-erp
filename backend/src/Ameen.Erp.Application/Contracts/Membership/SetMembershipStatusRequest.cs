using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetMembershipStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
