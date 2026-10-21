using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditItemRequest(Guid ExpectedVersion, string Code, string Name, ItemType Type, ItemTracking Tracking, Guid GroupId, Guid BaseUnitId,
    ItemUnitRequest[] Units, int? ShelfLifeDays, decimal? ReorderLevel, string? EnglishName, string? Notes, string Reason);
