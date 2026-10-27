using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed record FinancialMappingLineRequest(string Code,string Title,FinancialDisplaySign DisplaySign,
    Guid? AccountId=null,string? ParentCode=null,string? Formula=null);
