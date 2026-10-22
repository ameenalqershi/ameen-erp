namespace Ameen.Erp.Application;
public sealed record InventoryOpeningLineRequest(Guid ItemId, Guid UnitId, decimal Quantity, decimal UnitCost, Guid? IdentityId = null);
