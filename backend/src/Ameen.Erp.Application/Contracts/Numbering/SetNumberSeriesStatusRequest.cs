namespace Ameen.Erp.Application;
public sealed record SetNumberSeriesStatusRequest(Guid ExpectedVersion,bool IsActive,string Reason);
