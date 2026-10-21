using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record ItemGroupUsage(Guid GroupId, Guid GroupVersion, int DirectChildGroups, int ActiveDirectChildGroups,
    int DescendantGroups, int DirectItems, int ActiveDirectItems, int SubtreeItems, int ActiveSubtreeItems,
    bool StructureLocked, bool HasDeactivationBlockers);
