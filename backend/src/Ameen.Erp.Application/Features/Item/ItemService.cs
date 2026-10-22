using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class ItemService : ApplicationService
{
    private readonly ItemGroupService itemGroupService;
    public ItemService(IErpStore store, ITransactionRunner transactions, IClock clock, ItemGroupService itemGroupService) : base(store, transactions, clock)
    {
        this.itemGroupService = itemGroupService;
    }


    private static object ItemSnapshot(Item x) => new { x.Version, x.Revision, x.Code, x.Name, x.EnglishName, x.Notes, x.Type, x.Tracking,
        x.GroupId, x.BaseUnitId, x.ShelfLifeDays, x.ReorderLevel, x.IsActive, x.Units };
    private async Task ValidateItemReferences(Item item, CancellationToken ct)
    {
        var group = await Required<ItemGroup>(item.CompanyId, item.GroupId, ct);
        Check.That(group.IsActive, "inactive_item_group", "مجموعة الصنف معطلة.");
        await itemGroupService.ValidateGroupTree(item.CompanyId, group.Id, group.ParentId, ct); await itemGroupService.ValidateGroupAccounts(item.CompanyId, group, ct);
        Check.That(item.Type == ItemType.Service || (group.InventoryAccountId is not null && group.CostAccountId is not null),
            "stock_accounts_required", "الصنف المخزني يتطلب حسابي المخزون والتكلفة في المجموعة.");
        var unit = await Required<UnitOfMeasure>(item.CompanyId, item.BaseUnitId, ct);
        Check.That(unit.IsActive, "inactive_unit", "الوحدة معطلة.");
        Check.That(item.Tracking != ItemTracking.Serial || unit.QuantityScale == 0, "serial_unit_fraction", "الوحدة الأساسية للصنف المتسلسل لا تقبل كسورًا.");
        Check.That(item.ReorderLevel is null || decimal.Round(item.ReorderLevel.Value, unit.QuantityScale) == item.ReorderLevel,
            "quantity_precision", "حد إعادة الطلب يتجاوز دقة الوحدة الأساسية.");
    }
    private async Task<List<ItemUnit>> PrepareItemUnits(Item item, ItemUnitRequest[]? requests, CancellationToken ct)
    {
        Check.That(requests is not null && requests.Length is >= 1 and <= 100, "invalid_item_units", "أرسل وحدات الصنف متضمنة الأساسية.");
        var company = await Company(item.CompanyId, ct); var list = new List<ItemUnit>();
        var barcodes = new HashSet<string>(StringComparer.Ordinal);
        var baseUnit = await Required<UnitOfMeasure>(item.CompanyId, item.BaseUnitId, ct);
        foreach (var request in requests!)
        {
            Check.That(request is not null, "invalid_item_unit", "سطر الوحدة مطلوب.");
            var unit = await Required<UnitOfMeasure>(item.CompanyId, request!.UnitId, ct);
            Check.That(unit.IsActive, "inactive_unit", "الوحدة معطلة.");
            var line = new ItemUnit(item.CompanyId, item.Id, unit.Id, request.FactorToBase, request.Barcode, request.SalePrice);
            Check.That(decimal.Round(line.FactorToBase, baseUnit.QuantityScale) == line.FactorToBase, "conversion_precision", "معامل الوحدة لا ينتج كمية كاملة الدقة في الوحدة الأساسية.");
            Check.That(line.SalePrice is null || decimal.Round(line.SalePrice.Value, company.CurrencyScale) == line.SalePrice, "price_precision", "السعر يتجاوز دقة العملة الوظيفية.");
            if (line.Barcode is string barcode)
            {
                Check.That(barcodes.Add(barcode), "duplicate_barcode", "الباركود مكرر داخل الصنف.");
                Check.That(!(await store.Find<Item>(item.CompanyId, x => x.Id != item.Id && x.Units.Any(u => u.Barcode == barcode), ct)).Any(), "duplicate_barcode", "الباركود مستخدم في صنف آخر.", ErrorKind.Conflict);
            }
            list.Add(line);
        }
        return list;
    }
    public Task<CommandReceipt> CreateItem(Guid cid, Actor actor, string key, CreateItemRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "item.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var item = new Item(cid, request.Code, request.Name, request.Type, request.Tracking, request.GroupId, request.BaseUnitId, request.ShelfLifeDays, request.ReorderLevel, request.EnglishName, request.Notes);
            Check.That(!(await store.Find<Item>(cid, x => x.Code == item.Code, token)).Any(), "duplicate_item_code", "رمز الصنف مكرر.", ErrorKind.Conflict);
            await ValidateItemReferences(item, token); item.SetUnits(await PrepareItemUnits(item, request.Units, token));
            store.Add(item); Audit(cid, actor, "item.created", item, new { FunctionalCurrency = (await Company(cid, token)).FunctionalCurrency, after = ItemSnapshot(item) }); return Receipt(item, "Created");
        }, ct);
    public Task<CommandReceipt> EditItem(Guid cid, Guid id, Actor actor, string key, EditItemRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "item.edit:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var item = await Required<Item>(cid, id, token); Check.Version(item, request.ExpectedVersion); var reason = Check.Text(request.Reason, "السبب", 500);
            var candidate = new Item(cid, request.Code, request.Name, request.Type, request.Tracking, request.GroupId, request.BaseUnitId, request.ShelfLifeDays, request.ReorderLevel, request.EnglishName, request.Notes);
            var identities = await store.Find<InventoryIdentity>(cid, x => x.ItemId == id, token);
            Check.That(candidate.ShelfLifeDays is null || identities.All(x => x.ExpiryDate is not null), "inventory_expiry_required",
                "لا يمكن فرض مدة صلاحية مع وجود هويات مخزون بلا تاريخ انتهاء.");
            Check.That(identities.Count == 0 || candidate.Tracking == item.Tracking, "item_tracking_in_use",
                "لا يمكن تغيير نوع تتبع الصنف بعد إنشاء دفعات أو أرقام تسلسلية له.", ErrorKind.Conflict);
            Check.That(identities.Count == 0 || candidate.Type != ItemType.Service, "item_type_in_use",
                "لا يمكن تحويل صنف له دفعات أو أرقام تسلسلية إلى خدمة.", ErrorKind.Conflict);
            Check.That(!(await store.Find<Item>(cid, x => x.Id != id && x.Code == candidate.Code, token)).Any(), "duplicate_item_code", "رمز الصنف مكرر.", ErrorKind.Conflict);
            await ValidateItemReferences(candidate, token);
            var before = ItemSnapshot(item);
            item.Configure(candidate.Code, candidate.Name, candidate.Type, candidate.Tracking, candidate.GroupId, candidate.BaseUnitId, candidate.ShelfLifeDays, candidate.ReorderLevel, candidate.EnglishName, candidate.Notes);
            var lines = await PrepareItemUnits(item, request.Units, token);
            store.RemoveItemUnits(item.Units.ToArray()); item.SetUnits(lines);
            Audit(cid, actor, "item.edited", item, new { FunctionalCurrency = (await Company(cid, token)).FunctionalCurrency, before, after = ItemSnapshot(item), reason }); return Receipt(item, "Updated");
        }, ct);
    public Task<CommandReceipt> SetItemStatus(Guid cid, Guid id, Actor actor, string key, SetItemStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "item.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var item = await Required<Item>(cid, id, token); Check.Version(item, request.ExpectedVersion); var reason = Check.Text(request.Reason, "السبب", 500);
            if (request.IsActive) { await ValidateItemReferences(item, token); foreach (var line in item.Units) Check.That((await Required<UnitOfMeasure>(cid, line.UnitId, token)).IsActive, "inactive_unit", "وحدة الصنف معطلة."); }
            var before = ItemSnapshot(item); item.SetActive(request.IsActive); Audit(cid, actor, "item.status-changed", item, new { FunctionalCurrency = (await Company(cid, token)).FunctionalCurrency, before, after = ItemSnapshot(item), reason }); return Receipt(item, item.IsActive ? "Active" : "Inactive");
        }, ct);
    public async Task<Item> GetItem(Guid cid, Guid id, Actor actor, CancellationToken ct) { await Authorize(cid, actor, Permissions.Read, null, ct); return await Required<Item>(cid, id, ct); }
    public async Task<Page<Item>> SearchItems(Guid cid, Actor actor, string? search, Guid? groupId, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct); if (groupId is Guid group) await Required<ItemGroup>(cid, group, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح."); search = search?.Trim();
        return await store.ReadPage<Item>(cid, x => (groupId == null || x.GroupId == groupId) && (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search)) || x.Units.Any(u => u.Barcode == search)),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AuditEvent>> ListItemAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct); await GetItem(cid, id, actor, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id && (x.Action == "item.created" || x.Action == "item.edited" || x.Action == "item.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<ItemQuantityConversion> ConvertItemQuantity(Guid cid, Guid id, Actor actor, Guid unitId, decimal quantity, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct);
        return await ConvertItemQuantityCore(cid, id, unitId, quantity, ct);
    }
    internal async Task<ItemQuantityConversion> ConvertItemQuantityCore(Guid cid, Guid id, Guid unitId, decimal quantity, CancellationToken ct)
    {
        var item = await Required<Item>(cid, id, ct); Check.That(item.IsActive, "inactive_item", "الصنف معطل.");
        var line = item.Units.SingleOrDefault(x => x.UnitId == unitId); Check.That(line is not null, "item_unit_not_found", "الوحدة غير معرفة للصنف.");
        var unit = await Required<UnitOfMeasure>(cid, unitId, ct); var baseUnit = await Required<UnitOfMeasure>(cid, item.BaseUnitId, ct);
        Check.That(unit.IsActive && baseUnit.IsActive, "inactive_unit", "وحدة الصنف معطلة.");
        Check.That(quantity >= 0 && quantity < 100_000_000_000_000m && decimal.Round(quantity, unit.QuantityScale) == quantity, "quantity_precision", "كمية غير صالحة لدقة الوحدة.");
        decimal converted;
        try { converted = checked(quantity * line!.FactorToBase); } catch (OverflowException) { throw new BusinessException("quantity_overflow", "الكمية المحولة تتجاوز الحد."); }
        Check.That(converted < 100_000_000_000_000m, "quantity_overflow", "الكمية المحولة تتجاوز الحد.");
        Check.That(decimal.Round(converted, baseUnit.QuantityScale) == converted, "conversion_precision", "التحويل لا يمثل بدقة في الوحدة الأساسية.");
        return new(id, item.Version, unitId, item.BaseUnitId, quantity, line!.FactorToBase, converted);
    }
}
