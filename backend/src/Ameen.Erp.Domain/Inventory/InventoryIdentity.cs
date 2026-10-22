namespace Ameen.Erp.Domain;

/// <summary>
/// A lot or serial identity for a tracked item. One table keeps the INV05 card
/// contract uniform while the tracking discriminator preserves the operational
/// difference between a lot and a serial.
/// </summary>
public sealed class InventoryIdentity : CompanyEntity
{
    public Guid ItemId { get; private set; }
    public ItemTracking Tracking { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public DateOnly? ProductionDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public InventoryQualityStatus QualityStatus { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }

    private InventoryIdentity() { }

    public InventoryIdentity(Guid companyId, Guid itemId, ItemTracking tracking, string code, string name,
        DateOnly? productionDate, DateOnly? expiryDate, InventoryQualityStatus qualityStatus,
        string? englishName, string? notes) : base(companyId)
        => Configure(itemId, tracking, code, name, productionDate, expiryDate, qualityStatus, englishName, notes);

    public void Configure(Guid itemId, ItemTracking tracking, string code, string name, DateOnly? productionDate,
        DateOnly? expiryDate, InventoryQualityStatus qualityStatus, string? englishName, string? notes)
    {
        Check.That(itemId != Guid.Empty, "inventory_item_required", "الصنف مطلوب لهوية المخزون.");
        Check.That(tracking is ItemTracking.Lot or ItemTracking.Serial, "invalid_inventory_tracking",
            "هوية المخزون تقبل دفعة أو رقمًا تسلسليًا فقط.");
        Check.That(Enum.IsDefined(qualityStatus), "invalid_quality_status", "حالة الجودة غير معروفة.");
        Check.That(productionDate is null || expiryDate is null || expiryDate > productionDate,
            "invalid_inventory_expiry", "تاريخ الصلاحية يجب أن يكون بعد تاريخ الإنتاج.");

        ItemId = itemId;
        Tracking = tracking;
        Code = Check.Code(code, tracking == ItemTracking.Lot ? "رمز الدفعة" : "الرقم التسلسلي");
        Name = Check.Text(name, "اسم هوية المخزون");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        ProductionDate = productionDate;
        ExpiryDate = expiryDate;
        QualityStatus = qualityStatus;
        Advance();
    }

    public void SetActive(bool active) { IsActive = active; Advance(); }

    private void Advance()
    {
        Check.That(Revision < int.MaxValue, "inventory_identity_revision_exhausted",
            "بلغ إصدار هوية المخزون الحد الأقصى.", ErrorKind.Conflict);
        Revision++;
        Touch();
    }
}
