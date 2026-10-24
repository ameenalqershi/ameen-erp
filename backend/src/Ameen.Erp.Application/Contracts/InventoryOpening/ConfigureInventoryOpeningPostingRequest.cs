namespace Ameen.Erp.Application;
public sealed record ConfigureInventoryOpeningPostingRequest(Guid? ExpectedVersion, Guid ClearingAccountId, string Reason);
