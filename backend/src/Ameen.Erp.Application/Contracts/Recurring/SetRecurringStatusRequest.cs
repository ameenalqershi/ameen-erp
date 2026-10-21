using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetRecurringStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
