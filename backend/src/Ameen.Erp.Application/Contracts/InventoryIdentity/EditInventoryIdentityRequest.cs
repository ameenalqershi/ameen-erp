using Ameen.Erp.Domain;
using System.Text.Json.Serialization;

namespace Ameen.Erp.Application;

public sealed record EditInventoryIdentityRequest(Guid ExpectedVersion, string Code, string Name,
    DateOnly? ProductionDate, DateOnly? ExpiryDate, InventoryQualityStatus QualityStatus,
    string? EnglishName, string? Notes, string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LotNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SerialNumber = null);
