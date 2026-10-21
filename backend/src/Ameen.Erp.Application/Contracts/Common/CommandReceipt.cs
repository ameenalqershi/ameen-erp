using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CommandReceipt(Guid ResourceId, Guid Version, string Status, string? Number = null);
