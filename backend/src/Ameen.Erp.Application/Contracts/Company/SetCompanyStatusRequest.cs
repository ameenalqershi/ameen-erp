using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetCompanyStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
