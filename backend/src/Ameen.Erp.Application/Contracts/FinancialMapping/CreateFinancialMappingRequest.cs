using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed record CreateFinancialMappingRequest(string Name,FinancialStatementKind StatementKind,string VersionCode,
    DateOnly EffectiveFrom,FinancialMappingLineRequest[] Lines,Guid? BranchId=null);
