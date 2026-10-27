using System.Text.Json;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class InventoryOpeningService
{
    private async Task ValidateClearingAccount(Guid cid, Guid id, CancellationToken ct)
    {
        var company = await Company(cid, ct); var account = await Required<Account>(cid, id, ct);
        Check.That(account.IsActive && account.IsPosting && !account.IsControl && !account.RequiresCostCenter &&
            account.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity &&
            (account.CurrencyCode is null || account.CurrencyCode == company.FunctionalCurrency),
            "invalid_opening_clearing_account", "حساب التسوية يجب أن يكون حساب ميزانية تفصيليًا فعالًا غير رقابي وبعملة مناسبة ودون مركز إلزامي.");
    }
    public async Task<InventoryOpeningPostingSettings?> GetPostingSettings(Guid cid, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Configure, ct);
        return (await store.Find<InventoryOpeningPostingSettings>(cid, x => true, ct)).SingleOrDefault();
    }
    public Task<CommandReceipt> ConfigurePosting(Guid cid, Actor actor, string key, ConfigureInventoryOpeningPostingRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.posting-settings", request,
            token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var reason = Check.Text(request.Reason, "سبب الإعداد", 500); await ValidateClearingAccount(cid, request.ClearingAccountId, token);
            var settings = (await store.Find<InventoryOpeningPostingSettings>(cid, x => true, token)).SingleOrDefault();
            var before = settings is null ? null : new { settings.Version, settings.ClearingAccountId };
            if (settings is null)
            {
                Check.That(request.ExpectedVersion is null, "posting_settings_missing", "إعداد الترحيل غير موجود.", ErrorKind.Conflict);
                settings = new InventoryOpeningPostingSettings(cid, request.ClearingAccountId); store.Add(settings);
            }
            else { Check.Version(settings, request.ExpectedVersion ?? Guid.Empty); settings.Configure(request.ClearingAccountId); }
            Audit(cid, actor, "inventory-opening.settings-changed", settings, new { before, after = settings, reason });
            return Receipt(settings, "Configured");
        }, ct);
    private async Task<InventoryOpeningPostingPlan> PlanPosting(InventoryOpening opening, CancellationToken ct)
    {
        var settings = (await store.Find<InventoryOpeningPostingSettings>(opening.CompanyId, x => true, ct)).SingleOrDefault();
        Check.That(settings is not null, "opening_posting_not_configured", "حدد حساب تسوية الافتتاح أولًا.", ErrorKind.Conflict);
        await ValidateClearingAccount(opening.CompanyId, settings!.ClearingAccountId, ct);
        var warehouse = await Required<Warehouse>(opening.CompanyId, opening.WarehouseId, ct);
        Check.That(warehouse.Type == WarehouseType.Standard && !warehouse.TrackLocations,
            "opening_warehouse_policy_pending", "الترحيل الحالي لمخزن عادي دون توزيع مواقع؛ سياسة العزل والمواقع لم تكتمل.", ErrorKind.Conflict);
        var control = await Required<Account>(opening.CompanyId, warehouse.InventoryAccountId, ct);
        Check.That(control.IsActive && control.IsPosting && control.IsControl && control.Type == AccountType.Asset &&
            !control.RequiresCostCenter && (control.CurrencyCode is null || control.CurrencyCode == opening.CurrencyCode),
            "invalid_inventory_control_account", "حساب رقابة المخزن غير صالح للترحيل.");
        foreach (var group in opening.Lines.GroupBy(x => new { x.ItemId, x.IdentityId }))
        {
            var itemId = group.Key.ItemId; var identityId = group.Key.IdentityId;
            Check.That(!(await store.Find<StockMovement>(opening.CompanyId, x => x.WarehouseId == opening.WarehouseId &&
                x.ItemId == itemId && x.IdentityId == identityId, ct)).Any(), "inventory_opening_already_recorded",
                "سُجل افتتاح لهذا الصنف والهوية في المخزن؛ لا يسمح بازدواج الافتتاح.", ErrorKind.Conflict);
            if (identityId is Guid id)
            {
                var identity = await Required<InventoryIdentity>(opening.CompanyId, id, ct);
                Check.That(identity.QualityStatus == InventoryQualityStatus.Available &&
                    (identity.ExpiryDate is null || identity.ExpiryDate >= opening.AccountingDate),
                    "opening_identity_unavailable", "الهوية معزولة أو مرفوضة أو منتهية؛ سياسة ترحيلها غير متاحة.", ErrorKind.Conflict);
                if (identity.Tracking == ItemTracking.Serial)
                    Check.That(!(await store.Find<StockMovement>(opening.CompanyId, x => x.IdentityId == id && x.IsSerial, ct)).Any(),
                        "serial_already_in_stock", "التسلسل موجود في المخزون بالفعل.", ErrorKind.Conflict);
            }
            Check.That(group.Sum(x => x.BaseQuantity) < 100_000_000_000_000m, "quantity_overflow", "كمية الافتتاح المجمعة تتجاوز الحد.");
        }
        var value = opening.Lines.Sum(x => x.Value);
        Check.That(value > 0, "zero_opening_value_not_supported", "الترحيل الحالي يتطلب قيمة افتتاح موجبة.");
        return new(settings.Version, warehouse.Version, control.Id, settings.ClearingAccountId, value, value);
    }
    public Task<CommandReceipt> Post(Guid cid, Guid id, Actor actor, string key, PostInventoryOpeningRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.post:" + id, request,
            token => AuthorizeOpening(cid, id, actor, Permissions.InventoryPost, token), async token =>
        {
            var opening = await Required<InventoryOpening>(cid, id, token); Check.Version(opening, request.ExpectedVersion);
            Check.That(opening.ReversesOpeningId is null, "opening_reversal_post_required", "استخدم ترحيل مستند العكس.", ErrorKind.Conflict);
            Check.That(opening.State == InventoryOpeningState.Approved, "opening_state_conflict", "المستند غير معتمد.", ErrorKind.Conflict);
            await ValidateSnapshot(opening, token); var plan = await PlanPosting(opening, token);
            Check.That(plan.SettingsVersion == request.ExpectedSettingsVersion && plan.WarehouseVersion == request.ExpectedWarehouseVersion,
                "opening_posting_plan_changed", "تغير إعداد الترحيل أو المخزن؛ أعد المعاينة.", ErrorKind.Conflict);
            var company = await Company(cid, token); var number = await journals.NextNumber(cid, opening.FiscalPeriodId, opening.BranchId, actor.Subject, token);
            var journal = new JournalEntry(cid, opening.BranchId, opening.FiscalPeriodId, number.Number, number.Sequence,
                opening.DocumentDate, opening.AccountingDate, "افتتاح مخزون " + opening.Number, opening.Number, opening.CreatedBy, opening.CreatedAt);
            number.Allocation?.BindDocument(journal.Id);
            journal.SetInventoryOpeningSource(opening.Id);
            journal.SetLines(new[] {
                new JournalLine(cid, journal.Id, 1, plan.InventoryAccountId, null, "رقابة المخزون", opening.CurrencyCode, null, 1, plan.Debit, 0, company.CurrencyScale, company.CurrencyScale),
                new JournalLine(cid, journal.Id, 2, plan.ClearingAccountId, null, "تسوية افتتاح المخزون", opening.CurrencyCode, null, 1, 0, plan.Credit, company.CurrencyScale, company.CurrencyScale)
            });
            journal.Submit(); journal.Approve(opening.ApprovedBy!, true); journal.Post(clock.UtcNow); store.Add(journal);
            foreach (var group in opening.Lines.GroupBy(x => new { x.ItemId, x.IdentityId }))
                store.Add(new StockMovement(opening, journal.Id, plan.InventoryAccountId, group.Key.ItemId, group.Key.IdentityId,
                    group.Any(x => x.SerialNumber is not null), group.Sum(x => x.BaseQuantity), group.Sum(x => x.Value), clock.UtcNow));
            opening.Post(journal.Id, plan.InventoryAccountId, plan.ClearingAccountId, plan.SettingsVersion, actor.Subject, clock.UtcNow);
            Audit(cid, actor, "inventory-opening.posted", opening, new { opening.Number, opening.JournalId, plan, opening.Revision });
            Audit(cid, actor, "journal.post", journal, new { journal.Number, journal.InventoryOpeningId, Source = journal.Source, journal.Lines });
            store.Add(new OutboxEvent(cid, "ledger.journal-posted.v1", journal.Id, clock.UtcNow,
                JsonSerializer.Serialize(new { journal.Id, journal.Number, journal.BranchId, journal.AccountingDate, journal.InventoryOpeningId }, JsonDefaults.Options)));
            store.Add(new OutboxEvent(cid, "stock.inventory-opening-posted.v1", opening.Id, clock.UtcNow,
                JsonSerializer.Serialize(new { opening.Id, opening.Number, opening.JournalId, opening.WarehouseId, Value = plan.Debit }, JsonDefaults.Options)));
            return Receipt(opening, opening.State.ToString(), journal.Number);
        }, ct);
    public async Task<Page<StockMovement>> Movements(Guid cid, Actor actor, Guid? warehouseId, Guid? itemId, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        var allowed = await AllowedBranches(cid, member, null, ct);
        if (warehouseId is Guid wh) { var warehouse = await Required<Warehouse>(cid, wh, ct); await Authorize(cid, actor, Permissions.Read, warehouse.BranchId, ct); }
        if (itemId is Guid item) await Required<Item>(cid, item, ct);
        return await store.ReadPage<StockMovement>(cid, x => allowed.Contains(x.BranchId) && (warehouseId == null || x.WarehouseId == warehouseId) &&
            (itemId == null || x.ItemId == itemId), q => q.OrderBy(x => x.AccountingDate).ThenBy(x => x.Id), offset, limit, ct);
    }
}
