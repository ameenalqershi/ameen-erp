namespace Ameen.Erp.Application;
public sealed record EditNumberSeriesRequest(Guid ExpectedVersion,long NextNumber,string? Prefix=null);
