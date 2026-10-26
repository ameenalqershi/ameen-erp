namespace Ameen.Erp.Application;

public sealed record RunDueRecurringRequest(DateOnly AsOf, int? Limit = null);
