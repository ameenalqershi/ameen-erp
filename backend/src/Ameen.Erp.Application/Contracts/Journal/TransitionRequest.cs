using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record TransitionRequest(Guid ExpectedVersion, string? Reason = null);
