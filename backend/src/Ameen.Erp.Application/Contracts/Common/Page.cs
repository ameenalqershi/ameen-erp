using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record Page<T>(IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit);
