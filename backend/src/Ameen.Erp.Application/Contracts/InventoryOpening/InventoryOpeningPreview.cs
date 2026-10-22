namespace Ameen.Erp.Application;
public sealed record InventoryOpeningPreview(Guid Id, Guid ExpectedVersion, string CurrencyCode, decimal TotalValue,
    int LineCount, bool CanPost, string PostingBlocker, DateTimeOffset CheckedAt);
