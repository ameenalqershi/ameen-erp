using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record Actor(string Subject, bool IsPlatformAdministrator = false);
