namespace Ameen.Erp.Application;

public sealed record RunDueReversalsRequest(DateOnly AsOf, int? Limit = null);
