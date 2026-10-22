namespace Ameen.Erp.Application;
public sealed record EditInventoryOpeningRequest(Guid ExpectedVersion, string Description, InventoryOpeningLineRequest[] Lines, string Reason);
