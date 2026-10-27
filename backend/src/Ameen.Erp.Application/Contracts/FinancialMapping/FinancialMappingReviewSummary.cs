namespace Ameen.Erp.Application;
public sealed record FinancialMappingReviewSummary(Guid Id,Guid MappingVersion,DateOnly AsOf,string ReviewedBy,DateTimeOffset ReviewedAt,bool Ready);
