using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateBranchRequest(string Code, string Name, string City, string Address,
    Guid? ParentId = null, Guid? DefaultCostCenterId = null, string? EnglishName = null, string? Notes = null);
