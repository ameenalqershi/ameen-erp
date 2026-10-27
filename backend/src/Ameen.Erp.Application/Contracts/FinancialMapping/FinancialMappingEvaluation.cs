using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed record FinancialMappingEvaluation(string Name,FinancialStatementKind StatementKind,string VersionCode,
    DateOnly EffectiveFrom,DateOnly AsOf,string FunctionalCurrency,bool Ready,int RequiredAccountCount,int ClassifiedAccountCount,
    FinancialMappingRow[] Rows,FinancialMappingIssue[] Issues);
