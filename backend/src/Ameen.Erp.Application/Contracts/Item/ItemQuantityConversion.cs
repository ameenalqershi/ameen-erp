using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record ItemQuantityConversion(Guid ItemId, Guid ItemVersion, Guid UnitId, Guid BaseUnitId, decimal Quantity, decimal FactorToBase, decimal BaseQuantity);
