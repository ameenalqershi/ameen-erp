namespace Ameen.Erp.Application;
public sealed record SetFinancialMappingStatusRequest(Guid ExpectedVersion,bool IsActive,string Reason);
