using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record AccountUsageReference(string Kind, Guid ResourceId, Guid BranchId, string NumberOrName, string State);
