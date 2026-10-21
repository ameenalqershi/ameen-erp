using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditBranchRequest(Guid ExpectedVersion, string Code, string Name, string City, string Address,
    Guid? ParentId, Guid? DefaultCostCenterId, string? EnglishName, string? Notes, string Reason);
