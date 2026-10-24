namespace Ameen.Erp.Application;
public sealed record PostInventoryOpeningRequest(Guid ExpectedVersion, Guid ExpectedSettingsVersion, Guid ExpectedWarehouseVersion);
