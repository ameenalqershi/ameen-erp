using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

// All tenant reads require a company key; there is no unscoped generic query.
