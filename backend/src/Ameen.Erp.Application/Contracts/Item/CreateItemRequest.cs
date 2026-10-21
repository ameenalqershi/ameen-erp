using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateItemRequest(string Code, string Name, ItemType Type, ItemTracking Tracking, Guid GroupId, Guid BaseUnitId,
    ItemUnitRequest[] Units, int? ShelfLifeDays = null, decimal? ReorderLevel = null, string? EnglishName = null, string? Notes = null);
