using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

/// <summary>
/// INV05 lot/serial identity card. This is intentionally not a stock movement
/// service: quantities, costing, reservations and availability are added with
/// the inventory ledger.
/// </summary>
public sealed class InventoryIdentityService : ApplicationService
{
    public InventoryIdentityService(IErpStore store, ITransactionRunner transactions, IClock clock)
        : base(store, transactions, clock)
    {
    }

    private static object Snapshot(InventoryIdentity identity) => new
    {
        identity.Version, identity.Revision, identity.ItemId, identity.Tracking, identity.Code, identity.Name,
        identity.EnglishName, identity.Notes, identity.ProductionDate, identity.ExpiryDate, identity.QualityStatus,
        identity.IsActive, identity.LotNumber, identity.SerialNumber
    };

    private static string Label(ItemTracking tracking) => tracking == ItemTracking.Lot ? "الدفعة" : "الرقم التسلسلي";

    private static string Action(ItemTracking tracking, string verb) =>
        tracking == ItemTracking.Lot ? "inventory-lot." + verb : "inventory-serial." + verb;

    private static void ValidateItemPolicy(Item item, InventoryIdentity identity)
    {
        Check.That(item.Type != ItemType.Service, "service_has_no_inventory_identity",
            "الخدمة لا تحمل دفعة أو رقمًا تسلسليًا.");
        Check.That(item.Tracking == identity.Tracking, "item_tracking_mismatch",
            "نوع التتبع في بطاقة الصنف لا يطابق هوية المخزون.");
        Check.That(item.ShelfLifeDays is null || identity.ExpiryDate is not null, "inventory_expiry_required",
            "الصنف ذو مدة صلاحية يتطلب تاريخ انتهاء للدفعة أو الرقم التسلسلي.");
    }

    private async Task<Item> TrackedItem(Guid companyId, Guid itemId, ItemTracking tracking, CancellationToken ct)
    {
        var item = await Required<Item>(companyId, itemId, ct);
        Check.That(item.IsActive, "inactive_item", "الصنف معطل.");
        Check.That(item.Tracking == tracking, "item_tracking_mismatch",
            "المسار لا يطابق نوع تتبع الصنف.");
        Check.That(item.Type != ItemType.Service, "service_has_no_inventory_identity",
            "الخدمة لا تحمل دفعة أو رقمًا تسلسليًا.");
        return item;
    }

    private async Task EnsureUniqueCode(InventoryIdentity identity, Guid excludedId, CancellationToken ct)
    {
        var duplicate = await store.Find<InventoryIdentity>(identity.CompanyId,
            x => x.Id != excludedId && x.ItemId == identity.ItemId && x.Tracking == identity.Tracking &&
                 (x.Code == identity.Code ||
                  (identity.LotNumber != null && x.LotNumber == identity.LotNumber) ||
                  (identity.SerialNumber != null && x.SerialNumber == identity.SerialNumber)), ct);
        Check.That(duplicate.Count == 0, "duplicate_inventory_identity",
            $"{Label(identity.Tracking)} مكررة للصنف.", ErrorKind.Conflict);
    }

    private Task<CommandReceipt> Create(Guid companyId, Guid itemId, Actor actor, string key, ItemTracking tracking,
        CreateInventoryIdentityRequest request, CancellationToken ct) =>
        Command(companyId, actor, key, Action(tracking, "create") + ":" + itemId, request,
            token => AuthorizeGlobal(companyId, actor, Permissions.Configure, token), async token =>
            {
                var item = await TrackedItem(companyId, itemId, tracking, token);
                var identity = new InventoryIdentity(companyId, itemId, tracking, request.Code, request.Name,
                    request.ProductionDate, request.ExpiryDate, request.QualityStatus, request.EnglishName, request.Notes, request.LotNumber, request.SerialNumber);
                ValidateItemPolicy(item, identity);
                await EnsureUniqueCode(identity, Guid.Empty, token);
                store.Add(identity);
                Audit(companyId, actor, Action(tracking, "created"), identity,
                    new { itemCode = item.Code, after = Snapshot(identity) });
                return Receipt(identity, "Created");
            }, ct);

    private Task<CommandReceipt> Edit(Guid companyId, Guid itemId, Guid id, Actor actor, string key,
        ItemTracking tracking, EditInventoryIdentityRequest request, CancellationToken ct) =>
        Command(companyId, actor, key, Action(tracking, "edit:") + itemId + ":" + id, request,
            token => AuthorizeGlobal(companyId, actor, Permissions.Configure, token), async token =>
            {
                var item = await TrackedItem(companyId, itemId, tracking, token);
                var identity = await Required<InventoryIdentity>(companyId, id, token);
                Check.That(identity.ItemId == itemId && identity.Tracking == tracking, "reference_not_found",
                    "هوية المخزون لا تتبع الصنف المحدد.", ErrorKind.NotFound);
                Check.Version(identity, request.ExpectedVersion);
                var reason = Check.Text(request.Reason, "السبب", 500);
                var candidate = new InventoryIdentity(companyId, itemId, tracking, request.Code, request.Name,
                    request.ProductionDate, request.ExpiryDate, request.QualityStatus, request.EnglishName, request.Notes,
                    request.LotNumber ?? identity.LotNumber, request.SerialNumber ?? identity.SerialNumber);
                if ((await store.Find<StockMovement>(companyId, x => x.IdentityId == id, token)).Any())
                    Check.That(candidate.Code == identity.Code && candidate.LotNumber == identity.LotNumber && candidate.SerialNumber == identity.SerialNumber &&
                        candidate.ProductionDate == identity.ProductionDate && candidate.ExpiryDate == identity.ExpiryDate,
                        "inventory_identity_in_stock", "رقم الهوية وتواريخها مقفلة بعد حركة المخزون.", ErrorKind.Conflict);
                ValidateItemPolicy(item, candidate);
                await EnsureUniqueCode(candidate, identity.Id, token);
                var before = Snapshot(identity);
                identity.Configure(itemId, tracking, candidate.Code, candidate.Name, candidate.ProductionDate,
                    candidate.ExpiryDate, candidate.QualityStatus, candidate.EnglishName, candidate.Notes, candidate.LotNumber, candidate.SerialNumber);
                Audit(companyId, actor, Action(tracking, "edited"), identity,
                    new { itemCode = item.Code, before, after = Snapshot(identity), reason });
                return Receipt(identity, "Updated");
            }, ct);

    private Task<CommandReceipt> SetStatus(Guid companyId, Guid itemId, Guid id, Actor actor, string key,
        ItemTracking tracking, SetInventoryIdentityStatusRequest request, CancellationToken ct) =>
        Command(companyId, actor, key, Action(tracking, "status:") + itemId + ":" + id, request,
            token => AuthorizeGlobal(companyId, actor, Permissions.Configure, token), async token =>
            {
                var item = await TrackedItem(companyId, itemId, tracking, token);
                var identity = await Required<InventoryIdentity>(companyId, id, token);
                Check.That(identity.ItemId == itemId && identity.Tracking == tracking, "reference_not_found",
                    "هوية المخزون لا تتبع الصنف المحدد.", ErrorKind.NotFound);
                Check.Version(identity, request.ExpectedVersion);
                var reason = Check.Text(request.Reason, "السبب", 500);
                if (request.IsActive) ValidateItemPolicy(item, identity);
                var before = Snapshot(identity);
                identity.SetActive(request.IsActive);
                Audit(companyId, actor, Action(tracking, "status-changed"), identity,
                    new { itemCode = item.Code, before, after = Snapshot(identity), reason });
                return Receipt(identity, identity.IsActive ? "Active" : "Inactive");
            }, ct);

    private async Task<InventoryIdentity> Get(Guid companyId, Guid itemId, Guid id, Actor actor,
        ItemTracking tracking, CancellationToken ct)
    {
        await Authorize(companyId, actor, Permissions.Read, null, ct);
        await Required<Item>(companyId, itemId, ct);
        var identity = await Required<InventoryIdentity>(companyId, id, ct);
        Check.That(identity.ItemId == itemId && identity.Tracking == tracking, "reference_not_found",
            "هوية المخزون لا تتبع الصنف المحدد.", ErrorKind.NotFound);
        return identity;
    }

    private async Task<Page<InventoryIdentity>> Search(Guid companyId, Guid itemId, Actor actor, ItemTracking tracking,
        string? search, InventoryQualityStatus? qualityStatus, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        await Authorize(companyId, actor, Permissions.Read, null, ct);
        await Required<Item>(companyId, itemId, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح.");
        Check.That(qualityStatus is null || Enum.IsDefined(qualityStatus.Value), "invalid_quality_status", "حالة الجودة غير معروفة.");
        search = search?.Trim();
        return await store.ReadPage<InventoryIdentity>(companyId,
            x => x.ItemId == itemId && x.Tracking == tracking &&
                 (qualityStatus == null || x.QualityStatus == qualityStatus) &&
                 (isActive == null || x.IsActive == isActive) &&
                 (search == null || x.Code.Contains(search) || x.Name.Contains(search) ||
                  (x.LotNumber != null && x.LotNumber.Contains(search)) ||
                  (x.SerialNumber != null && x.SerialNumber.Contains(search)) ||
                  (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }

    private async Task<Page<AuditEvent>> AuditPage(Guid companyId, Guid itemId, Guid id, Actor actor,
        ItemTracking tracking, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        await AuthorizeGlobal(companyId, actor, Permissions.Audit, ct);
        await Get(companyId, itemId, id, actor, tracking, ct);
        var created = Action(tracking, "created");
        var edited = Action(tracking, "edited");
        var status = Action(tracking, "status-changed");
        return await store.ReadPage<AuditEvent>(companyId,
            x => x.ResourceId == id && (x.Action == created || x.Action == edited || x.Action == status),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }

    public Task<CommandReceipt> CreateLot(Guid companyId, Guid itemId, Actor actor, string key,
        CreateInventoryIdentityRequest request, CancellationToken ct) =>
        Create(companyId, itemId, actor, key, ItemTracking.Lot, request, ct);

    public Task<CommandReceipt> CreateSerial(Guid companyId, Guid itemId, Actor actor, string key,
        CreateInventoryIdentityRequest request, CancellationToken ct) =>
        Create(companyId, itemId, actor, key, ItemTracking.Serial, request, ct);

    public Task<CommandReceipt> EditLot(Guid companyId, Guid itemId, Guid id, Actor actor, string key,
        EditInventoryIdentityRequest request, CancellationToken ct) =>
        Edit(companyId, itemId, id, actor, key, ItemTracking.Lot, request, ct);

    public Task<CommandReceipt> EditSerial(Guid companyId, Guid itemId, Guid id, Actor actor, string key,
        EditInventoryIdentityRequest request, CancellationToken ct) =>
        Edit(companyId, itemId, id, actor, key, ItemTracking.Serial, request, ct);

    public Task<CommandReceipt> SetLotStatus(Guid companyId, Guid itemId, Guid id, Actor actor, string key,
        SetInventoryIdentityStatusRequest request, CancellationToken ct) =>
        SetStatus(companyId, itemId, id, actor, key, ItemTracking.Lot, request, ct);

    public Task<CommandReceipt> SetSerialStatus(Guid companyId, Guid itemId, Guid id, Actor actor, string key,
        SetInventoryIdentityStatusRequest request, CancellationToken ct) =>
        SetStatus(companyId, itemId, id, actor, key, ItemTracking.Serial, request, ct);

    public Task<InventoryIdentity> GetLot(Guid companyId, Guid itemId, Guid id, Actor actor, CancellationToken ct) =>
        Get(companyId, itemId, id, actor, ItemTracking.Lot, ct);

    public Task<InventoryIdentity> GetSerial(Guid companyId, Guid itemId, Guid id, Actor actor, CancellationToken ct) =>
        Get(companyId, itemId, id, actor, ItemTracking.Serial, ct);

    public Task<Page<InventoryIdentity>> SearchLots(Guid companyId, Guid itemId, Actor actor, string? search,
        InventoryQualityStatus? qualityStatus, bool? isActive, int offset, int limit, CancellationToken ct) =>
        Search(companyId, itemId, actor, ItemTracking.Lot, search, qualityStatus, isActive, offset, limit, ct);

    public Task<Page<InventoryIdentity>> SearchSerials(Guid companyId, Guid itemId, Actor actor, string? search,
        InventoryQualityStatus? qualityStatus, bool? isActive, int offset, int limit, CancellationToken ct) =>
        Search(companyId, itemId, actor, ItemTracking.Serial, search, qualityStatus, isActive, offset, limit, ct);

    public Task<Page<AuditEvent>> ListLotAudit(Guid companyId, Guid itemId, Guid id, Actor actor, int offset,
        int limit, CancellationToken ct) =>
        AuditPage(companyId, itemId, id, actor, ItemTracking.Lot, offset, limit, ct);

    public Task<Page<AuditEvent>> ListSerialAudit(Guid companyId, Guid itemId, Guid id, Actor actor, int offset,
        int limit, CancellationToken ct) =>
        AuditPage(companyId, itemId, id, actor, ItemTracking.Serial, offset, limit, ct);
}
