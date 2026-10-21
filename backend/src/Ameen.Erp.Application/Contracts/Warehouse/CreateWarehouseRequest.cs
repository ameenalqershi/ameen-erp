using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateWarehouseRequest(string Code, string Name, Guid BranchId, Guid KeeperMembershipId, Guid InventoryAccountId,
    WarehouseType Type, bool TrackLocations, WarehouseLocationRequest[] Locations, string? EnglishName = null, string? Notes = null);
