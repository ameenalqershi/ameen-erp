using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record ItemUnitRequest(Guid UnitId, decimal FactorToBase, string? Barcode = null, decimal? SalePrice = null);
