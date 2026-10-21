using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record FiscalPeriodRequest(string Code, DateOnly StartsOn, DateOnly EndsOn);
