namespace Ameen.Erp.Application;

// Counts only references inside the authorized company. A disabled membership remains visible
// for audit/usage, but this DTO must not expose credentials or secrets held by the IdP.
public sealed record MembershipUsage(
    Guid MembershipId, string Subject, bool IsActive,
    int AssignedRoleCount, int WarehouseKeeperCount, int CostCenterResponsibilityCount,
    int JournalCreatorCount, int RecurringTemplateCreatorCount, bool HasReferences);
