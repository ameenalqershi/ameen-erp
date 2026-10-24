using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;

public sealed partial class InventoryOpeningService : ApplicationService
{
    private readonly BranchService branches;
    private readonly ItemService items;
    private readonly JournalService journals;
    private readonly CurrencyService currencies;
    public InventoryOpeningService(IErpStore store, ITransactionRunner transactions, IClock clock,
        BranchService branches, ItemService items, JournalService journals, CurrencyService currencies) : base(store, transactions, clock)
    { this.branches = branches; this.items = items; this.journals = journals; this.currencies = currencies; }

    private async Task AuthorizeOpening(Guid cid, Guid id, Actor actor, string permission, CancellationToken ct)
    {
        await Authorize(cid, actor, permission, null, ct);
        var opening = await Required<InventoryOpening>(cid, id, ct);
        await Authorize(cid, actor, permission, opening.BranchId, ct);
    }
    private async Task ValidateHeader(InventoryOpening opening, CancellationToken ct)
    {
        var company = await Company(opening.CompanyId, ct);
        Check.That(company.FunctionalCurrency == opening.CurrencyCode, "opening_functional_currency_only", "الافتتاح يدعم العملة الوظيفية حاليًا.");
        await currencies.RequireCurrency(opening.CompanyId, opening.CurrencyCode, ct);
        await branches.ActiveBranch(opening.CompanyId, opening.BranchId, ct);
        var period = await journals.OpenPeriod(opening.CompanyId, opening.AccountingDate, ct);
        Check.That(period.Id == opening.FiscalPeriodId, "period_mismatch", "الفترة لا تطابق المستند.");
        var warehouse = await Required<Warehouse>(opening.CompanyId, opening.WarehouseId, ct);
        Check.That(warehouse.IsActive && warehouse.BranchId == opening.BranchId, "opening_warehouse_mismatch", "المخزن معطل أو تابع لفرع آخر.");
    }
    private async Task<List<InventoryOpeningLine>> Prepare(InventoryOpening opening, InventoryOpeningLineRequest[]? requests, CancellationToken ct)
    {
        Check.That(requests is not null && requests.Length is >= 1 and <= 200, "invalid_opening_lines", "المستند يتكون من 1 إلى 200 سطر.");
        var company = await Company(opening.CompanyId, ct); var lines = new List<InventoryOpeningLine>(); var serials = new HashSet<Guid>();
        foreach (var request in requests!)
        {
            Check.That(request is not null, "invalid_line", "سطر فارغ.");
            var item = await Required<Item>(opening.CompanyId, request!.ItemId, ct);
            Check.That(item.IsActive && item.Type != ItemType.Service, "opening_item_not_stock", "الصنف معطل أو خدمة.");
            var conversion = await items.ConvertItemQuantityCore(opening.CompanyId, item.Id, request.UnitId, request.Quantity, ct);
            var unit = await Required<UnitOfMeasure>(opening.CompanyId, request.UnitId, ct);
            InventoryIdentity? identity = null;
            if (item.Tracking == ItemTracking.None)
                Check.That(request.IdentityId is null, "opening_identity_unexpected", "الصنف غير متتبع.");
            else
            {
                Check.That(request.IdentityId is not null, "opening_identity_required", "هوية الدفعة أو التسلسل مطلوبة.");
                identity = await Required<InventoryIdentity>(opening.CompanyId, request.IdentityId!.Value, ct);
                Check.That(identity.IsActive && identity.ItemId == item.Id && identity.Tracking == item.Tracking,
                    "opening_identity_mismatch", "هوية المخزون معطلة أو لا تخص الصنف ونوع تتبعه.");
                if (item.Tracking == ItemTracking.Serial)
                {
                    Check.That(conversion.BaseQuantity == 1, "opening_serial_quantity", "كل تسلسل يساوي وحدة أساسية واحدة.");
                    Check.That(serials.Add(identity.Id), "opening_duplicate_serial", "التسلسل مكرر في المستند.");
                }
            }
            lines.Add(new(opening.CompanyId, opening.Id, lines.Count + 1, item, unit, identity,
                request.Quantity, conversion.FactorToBase, conversion.BaseQuantity, request.UnitCost, company.CurrencyScale));
        }
        return lines;
    }
    private async Task ValidateSnapshot(InventoryOpening opening, CancellationToken ct)
    {
        if (opening.ReversesOpeningId is not null) { await ValidateReversal(opening, ct); return; }
        await ValidateHeader(opening, ct);
        var current = await Prepare(opening, opening.Lines.OrderBy(x => x.Position)
            .Select(x => new InventoryOpeningLineRequest(x.ItemId, x.UnitId, x.Quantity, x.UnitCost, x.IdentityId)).ToArray(), ct);
        foreach (var line in current)
        {
            var saved = opening.Lines.Single(x => x.Position == line.Position);
            Check.That(saved.ItemVersion == line.ItemVersion && saved.IdentityVersion == line.IdentityVersion &&
                saved.BaseQuantity == line.BaseQuantity && saved.FactorToBase == line.FactorToBase && saved.Value == line.Value,
                "opening_reference_changed", "تغيرت بطاقة مرجعية؛ أعد المستند للتصحيح ثم احفظه.", ErrorKind.Conflict);
        }
    }
    public Task<CommandReceipt> Create(Guid cid, Actor actor, string key, CreateInventoryOpeningRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.create", request,
            token => Authorize(cid, actor, Permissions.InventoryWrite, request.BranchId, token), async token =>
        {
            var company = await Company(cid, token);
            Check.That(Check.Currency(request.CurrencyCode) == company.FunctionalCurrency && request.ExchangeRate == 1,
                "opening_functional_currency_only", "الافتتاح يدعم العملة الوظيفية وسعر 1 حاليًا.");
            var period = await journals.OpenPeriod(cid, request.AccountingDate, token);
            var year = await Required<FiscalYear>(cid, period.FiscalYearId, token); var scope = "IO:" + year.Id;
            var sequence = (await store.Find<NumberSequence>(cid, x => x.Scope == scope, token)).SingleOrDefault();
            if (sequence is null) { sequence = new NumberSequence(cid, scope); store.Add(sequence); }
            var opening = new InventoryOpening(cid, request.BranchId, request.WarehouseId, period, $"IO-{year.Code}-{sequence.Next():000000}",
                request.DocumentDate, request.AccountingDate, request.CutoffDate, request.CurrencyCode, request.CountReference,
                request.Description, request.ExternalReference, actor.Subject, clock.UtcNow);
            await ValidateHeader(opening, token); opening.Edit(request.Description, await Prepare(opening, request.Lines, token), clock.UtcNow);
            store.Add(opening); Audit(cid, actor, "inventory-opening.created", opening, opening);
            return Receipt(opening, opening.State.ToString(), opening.Number);
        }, ct);
    public Task<CommandReceipt> Edit(Guid cid, Guid id, Actor actor, string key, EditInventoryOpeningRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.edit:" + id, request,
            token => AuthorizeOpening(cid, id, actor, Permissions.InventoryWrite, token), async token =>
        {
            var opening = await Required<InventoryOpening>(cid, id, token); Check.Version(opening, request.ExpectedVersion);
            Check.That(opening.ReversesOpeningId is null, "opening_reversal_immutable", "سطور العكس وتواريخه تاريخية ثابتة؛ ألغِ المسودة لإنشاء عكس آخر.", ErrorKind.Conflict);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500); await ValidateHeader(opening, token);
            var before = new { opening.Description, Lines = opening.Lines.ToArray(), opening.State, opening.Version };
            var lines = await Prepare(opening, request.Lines, token);
            var old = opening.Lines.ToArray(); opening.Edit(request.Description, lines, clock.UtcNow); store.RemoveInventoryOpeningLines(old);
            Audit(cid, actor, "inventory-opening.edited", opening, new { before, after = opening, reason });
            return Receipt(opening, opening.State.ToString(), opening.Number);
        }, ct);
    public Task<CommandReceipt> Transition(Guid cid, Guid id, Actor actor, string key, TransitionRequest request, string action, CancellationToken ct)
    {
        Check.That(action is "submit" or "return" or "approve" or "cancel", "invalid_transition", "الإجراء غير متاح.");
        var permission = action is "approve" or "return" ? Permissions.InventoryApprove : Permissions.InventoryWrite;
        return Command(cid, actor, key, "inventory-opening." + action + ":" + id, request,
            token => AuthorizeOpening(cid, id, actor, permission, token), async token =>
        {
            var opening = await Required<InventoryOpening>(cid, id, token); Check.Version(opening, request.ExpectedVersion);
            if (action is "return" or "cancel") Check.Text(request.Reason, "سبب الإجراء", 500);
            if (action is "submit" or "approve") await ValidateSnapshot(opening, token);
            var before = opening.State; opening.Transition(action, actor.Subject, clock.UtcNow);
            Audit(cid, actor, "inventory-opening." + action, opening, new { before, after = opening.State, request.Reason, opening.Revision });
            return Receipt(opening, opening.State.ToString(), opening.Number);
        }, ct);
    }
}
