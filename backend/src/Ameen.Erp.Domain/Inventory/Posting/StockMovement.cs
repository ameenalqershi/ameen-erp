namespace Ameen.Erp.Domain;
// Initial ledger source: inventory opening. Later movement sources require their own immutable provenance.
public sealed class StockMovement : CompanyEntity
{
    public Guid OpeningId { get; private set; }
    public Guid JournalId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? IdentityId { get; private set; }
    public bool IsSerial { get; private set; }
    public Guid InventoryAccountId { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public decimal Value { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public DateOnly AccountingDate { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    private StockMovement() { }
    public StockMovement(InventoryOpening opening, Guid journalId, Guid inventoryAccountId, Guid itemId,
        Guid? identityId, bool isSerial, decimal baseQuantity, decimal value, DateTimeOffset now) : base(opening.CompanyId)
    {
        Check.That(baseQuantity > 0 && baseQuantity < 100_000_000_000_000m && decimal.Round(baseQuantity, 6) == baseQuantity &&
            value >= 0 && value < 100_000_000_000_000m && decimal.Round(value, 6) == value,
            "invalid_stock_movement", "كمية أو قيمة حركة المخزون غير صالحة.");
        Check.That(!isSerial || identityId is not null && baseQuantity == 1, "opening_serial_quantity", "التسلسل يساوي وحدة أساسية واحدة.");
        Check.That(journalId != Guid.Empty && inventoryAccountId != Guid.Empty && itemId != Guid.Empty, "invalid_stock_reference", "مراجع الحركة مطلوبة.");
        OpeningId = opening.Id; JournalId = journalId; BranchId = opening.BranchId; WarehouseId = opening.WarehouseId;
        ItemId = itemId; IdentityId = identityId; IsSerial = isSerial; InventoryAccountId = inventoryAccountId;
        BaseQuantity = baseQuantity; Value = value; CurrencyCode = opening.CurrencyCode; AccountingDate = opening.AccountingDate; OccurredAt = now;
    }
}
