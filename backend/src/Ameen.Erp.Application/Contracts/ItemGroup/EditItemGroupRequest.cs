using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditItemGroupRequest(Guid ExpectedVersion, string Code, string Name, Guid SalesAccountId, Guid? ParentId,
    Guid? InventoryAccountId, Guid? CostAccountId, string? EnglishName, string? Notes, string Reason);
