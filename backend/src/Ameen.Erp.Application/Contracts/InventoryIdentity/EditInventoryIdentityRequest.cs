using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditInventoryIdentityRequest(Guid ExpectedVersion, string Code, string Name,
    DateOnly? ProductionDate, DateOnly? ExpiryDate, InventoryQualityStatus QualityStatus,
    string? EnglishName, string? Notes, string Reason);
