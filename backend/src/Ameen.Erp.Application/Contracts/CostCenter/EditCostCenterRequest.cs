using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditCostCenterRequest(Guid ExpectedVersion, string Code, string Name, Guid? BranchId,
    Guid? ParentId, Guid? ResponsibleMembershipId, string? EnglishName, string? Notes, string Reason);
