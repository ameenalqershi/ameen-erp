using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateInventoryIdentityRequest(string Code, string Name, DateOnly? ProductionDate,
    DateOnly? ExpiryDate, InventoryQualityStatus QualityStatus, string? EnglishName = null, string? Notes = null);
