using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public interface IClock { DateTimeOffset UtcNow { get; } }
