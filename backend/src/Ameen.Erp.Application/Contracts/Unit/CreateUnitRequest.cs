using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateUnitRequest(string Code, string Name, string StandardCode, int QuantityScale, string Description, string? EnglishName = null, string? Notes = null);
