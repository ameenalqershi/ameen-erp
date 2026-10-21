using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateFiscalYearRequest(string Code, DateOnly StartsOn, DateOnly EndsOn, FiscalPeriodRequest[]? Periods = null);
