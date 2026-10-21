using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateCostCenterRequest(string Code, string Name, Guid? BranchId, Guid? ParentId = null,
    Guid? ResponsibleMembershipId = null, string? EnglishName = null, string? Notes = null);
