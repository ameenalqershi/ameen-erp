using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed record FinancialMappingRow(string Code,string? ParentCode,string Title,FinancialDisplaySign DisplaySign,
    string? Formula,decimal Amount,Guid[] AccountIds);
