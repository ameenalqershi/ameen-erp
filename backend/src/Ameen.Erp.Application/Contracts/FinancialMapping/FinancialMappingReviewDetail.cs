namespace Ameen.Erp.Application;
public sealed record FinancialMappingReviewDetail(Guid Id,Guid MappingId,Guid MappingVersion,DateOnly AsOf,
    string ReviewedBy,DateTimeOffset ReviewedAt,FinancialMappingEvaluation Evaluation);
