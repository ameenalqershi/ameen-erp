using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateItemGroupRequest(string Code, string Name, Guid SalesAccountId, Guid? ParentId = null,
    Guid? InventoryAccountId = null, Guid? CostAccountId = null, string? EnglishName = null, string? Notes = null);
