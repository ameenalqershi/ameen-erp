using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record ChangePeriodRequest(Guid ExpectedVersion, string Reason);
