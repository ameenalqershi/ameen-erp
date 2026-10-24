namespace Ameen.Erp.Application;
public sealed record ReverseInventoryOpeningRequest(Guid ExpectedVersion, DateOnly AccountingDate, string Reason);
