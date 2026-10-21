using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditWarehouseRequest(Guid ExpectedVersion, string Code, string Name, Guid BranchId, Guid KeeperMembershipId, Guid InventoryAccountId,
    WarehouseType Type, bool TrackLocations, WarehouseLocationRequest[] Locations, string? EnglishName, string? Notes, string Reason);
