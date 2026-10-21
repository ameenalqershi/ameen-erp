using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditUnitRequest(Guid ExpectedVersion, string Code, string Name, string StandardCode, int QuantityScale, string Description, string? EnglishName, string? Notes, string Reason);
