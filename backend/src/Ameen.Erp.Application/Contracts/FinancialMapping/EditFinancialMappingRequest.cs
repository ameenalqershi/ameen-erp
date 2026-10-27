using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed record EditFinancialMappingRequest(Guid ExpectedVersion,string Name,FinancialStatementKind StatementKind,
    string VersionCode,DateOnly EffectiveFrom,FinancialMappingLineRequest[] Lines);
