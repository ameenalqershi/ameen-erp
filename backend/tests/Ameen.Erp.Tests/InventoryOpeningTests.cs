using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class InventoryOpeningTests
{
    public static Actor Writer { get; } = new("stock-writer");
    public static async Task<CreateInventoryOpeningRequest> Setup(Fixture f, ItemTracking tracking = ItemTracking.None)
    {
        await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(Writer.Subject, new[] { Permissions.Read, Permissions.InventoryWrite, Permissions.InventoryApprove }, true, Array.Empty<Guid>(), null), default);
        var itemRequest = (await ItemTests.Setup(f)) with { Tracking = tracking };
        var item = await f.Service.Item.CreateItem(f.CompanyId, f.Owner, Fixture.Key(), itemRequest, default);
        Guid? identity = null;
        if (tracking != ItemTracking.None)
        {
            var r = new CreateInventoryIdentityRequest("ID-1", "هوية", null, null, InventoryQualityStatus.Available);
            identity = (tracking == ItemTracking.Lot
                ? await f.Service.InventoryIdentity.CreateLot(f.CompanyId, item.ResourceId, f.Owner, Fixture.Key(), r, default)
                : await f.Service.InventoryIdentity.CreateSerial(f.CompanyId, item.ResourceId, f.Owner, Fixture.Key(), r, default)).ResourceId;
        }
        var wh = await f.Service.Warehouse.CreateWarehouse(f.CompanyId, f.Owner, Fixture.Key(), await WarehouseTests.Setup(f), default);
        return new(f.BranchId, wh.ResourceId, Fixture.Date, Fixture.Date, Fixture.Date, "SAR", 1, "COUNT-1", "افتتاح",
            new[] { new InventoryOpeningLineRequest(item.ResourceId, itemRequest.BaseUnitId, tracking == ItemTracking.Serial ? 1 : 2, 5.25m, identity) });
    }
    public static Task<CommandReceipt> Create(Fixture f, CreateInventoryOpeningRequest request, string? key = null) => f.Service.InventoryOpening.Create(f.CompanyId, Writer, key ?? Fixture.Key(), request, default);
    public static Task<InventoryOpening> Get(Fixture f, Guid id) => f.Service.InventoryOpening.Get(f.CompanyId, id, f.Owner, default);
    public static Task<CommandReceipt> Action(Fixture f, CommandReceipt r, string action, Actor? actor = null) =>
        f.Service.InventoryOpening.Transition(f.CompanyId, r.ResourceId, actor ?? Writer, Fixture.Key(), new(r.Version, "سبب موثق"), action, default);
    public static async Task<Actor> Reviewer(Fixture f)
    {
        var actor = new Actor("stock-reviewer");
        await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(actor.Subject, new[] { Permissions.Read, Permissions.InventoryApprove }, true, Array.Empty<Guid>(), null), default);
        return actor;
    }
    private static InventoryOpening DirectOpening(Fixture f, CreateInventoryOpeningRequest request,
        string actor, DateTimeOffset now)
    {
        var period = f.Store.All<FiscalPeriod>().Single(x =>
            x.StartsOn <= request.AccountingDate && x.EndsOn >= request.AccountingDate);
        return new InventoryOpening(f.CompanyId, request.BranchId, request.WarehouseId, period,
            "IO-AUDIT-TEST", request.DocumentDate, request.AccountingDate, request.CutoffDate,
            request.CurrencyCode, request.CountReference, request.Description, null, actor, now);
    }

    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV06-FG07: generated creator and creation timestamp are required before mutation", async f =>
        {
            var request = await Setup(f);
            var when = DateTimeOffset.UtcNow;
            var good = DirectOpening(f, request, " audit-writer ", when);
            Ensure(good.CreatedBy == "audit-writer" && good.CreatedAt == when &&
                good.ModifiedAt == when && good.State == InventoryOpeningState.Draft);
            await Error("invalid_text", () => Task.FromResult(DirectOpening(f, request, " ", when)));
            await Error("opening_audit_time_required",
                () => Task.FromResult(DirectOpening(f, request, "audit-writer", default)));
            Ensure(f.Store.Count<InventoryOpening>() == 0 && f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 && f.Store.Count<OutboxEvent>() == 0);
        });
        test("INV06-FG07: invalid audit actor and timestamp cannot approve or post opening", async f =>
        {
            var request = await Setup(f);
            var now = DateTimeOffset.UtcNow;
            var opening = DirectOpening(f, request, Writer.Subject, now);
            var initialVersion = opening.Version;
            await Error("opening_audit_time_required", () =>
            { opening.Transition("submit", Writer.Subject, default); return Task.CompletedTask; });
            await Error("invalid_text", () =>
            { opening.Transition("submit", " ", now); return Task.CompletedTask; });
            Ensure(opening.State == InventoryOpeningState.Draft && opening.Version == initialVersion);
            opening.Transition("submit", Writer.Subject, now);
            await Error("invalid_text", () =>
            { opening.Transition("approve", " ", now); return Task.CompletedTask; });
            await Error("opening_audit_time_required", () =>
            { opening.Transition("approve", "reviewer", default); return Task.CompletedTask; });
            Ensure(opening.State == InventoryOpeningState.Submitted && opening.ApprovedBy is null);
            opening.Transition("approve", "reviewer", now);
            var journalId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var clearingId = Guid.NewGuid();
            var settingId = Guid.NewGuid();
            await Error("invalid_text", () =>
            { opening.Post(journalId, accountId, clearingId, settingId, " ", now); return Task.CompletedTask; });
            await Error("opening_audit_time_required", () =>
            { opening.Post(journalId, accountId, clearingId, settingId, "poster", default); return Task.CompletedTask; });
            Ensure(opening.State == InventoryOpeningState.Approved && opening.JournalId is null &&
                opening.PostedAt is null && opening.PostedBy is null);
            opening.Post(journalId, accountId, clearingId, settingId, "poster", now);
            Ensure(opening.State == InventoryOpeningState.Posted && opening.PostedBy == "poster" &&
                opening.JournalId == journalId && opening.PostedAt == now);
            Ensure(f.Store.Count<InventoryOpening>() == 0 && f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 && f.Store.Count<OutboxEvent>() == 0);
        });
        test("INV06-FG07: exhausted revision rejects draft edit and submit without partial mutation", async f =>
        {
            var request = await Setup(f);
            var now = DateTimeOffset.UtcNow;
            var opening = DirectOpening(f, request, Writer.Subject, now);
            var item = f.Store.All<Item>().Single(x => x.Id == request.Lines.Single().ItemId);
            var unit = f.Store.All<UnitOfMeasure>().Single(x => x.Id == request.Lines.Single().UnitId);
            var line = new InventoryOpeningLine(f.CompanyId, opening.Id, 1, item, unit, null,
                2m, 1m, 2m, 5.25m, 2);
            typeof(InventoryOpening).GetProperty(nameof(InventoryOpening.Revision))!
                .SetValue(opening, int.MaxValue);
            var initialVersion = opening.Version;
            var initialDescription = opening.Description;
            await Error("opening_revision_exhausted", () =>
            { opening.Edit("تعديل يجب رفضه", new[] { line }, now); return Task.CompletedTask; });
            await Error("opening_revision_exhausted", () =>
            { opening.Transition("submit", Writer.Subject, now); return Task.CompletedTask; });
            Ensure(opening.State == InventoryOpeningState.Draft && opening.Description == initialDescription &&
                opening.Lines.Count == 0 && opening.ApprovedBy is null &&
                opening.Version == initialVersion && opening.ModifiedAt == now);
        });
        test("INV06-FG07: exhausted revision cannot leave partially posted opening", async f =>
        {
            var request = await Setup(f);
            var now = DateTimeOffset.UtcNow;
            var opening = DirectOpening(f, request, Writer.Subject, now);
            opening.Transition("submit", Writer.Subject, now);
            opening.Transition("approve", "reviewer", now);
            typeof(InventoryOpening).GetProperty(nameof(InventoryOpening.Revision))!
                .SetValue(opening, int.MaxValue);
            var initialVersion = opening.Version;
            await Error("opening_revision_exhausted", () =>
            {
                opening.Post(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    "poster", now);
                return Task.CompletedTask;
            });
            Ensure(opening.State == InventoryOpeningState.Approved && opening.JournalId is null &&
                opening.PostedAt is null && opening.PostedBy is null &&
                opening.Version == initialVersion && opening.ApprovedBy == "reviewer");
        });
        test("INV06-OP05: invalid reversal reason must not reserve source or advance audit version", async f =>
        {
            var request = await Setup(f);
            var opening = DirectOpening(f, request, Writer.Subject, DateTimeOffset.UtcNow);
            var sourceId = Guid.NewGuid();
            var originalVersion = opening.Version;
            await Error("invalid_text", () =>
            { opening.SetReversalSource(sourceId, " "); return Task.CompletedTask; });
            await Error("invalid_text", () =>
            { opening.SetReversalSource(sourceId, new string('x', 501)); return Task.CompletedTask; });
            await Error("opening_reversal_source_conflict", () =>
            { opening.SetReversalSource(Guid.Empty, "سبب صحيح"); return Task.CompletedTask; });
            Ensure(opening.State == InventoryOpeningState.Draft &&
                opening.ReversesOpeningId is null && opening.ReversalReason is null &&
                opening.Version == originalVersion);
            opening.SetReversalSource(sourceId, "سبب صحيح");
            Ensure(opening.ReversesOpeningId == sourceId && opening.ReversalReason == "سبب صحيح" &&
                opening.Version != originalVersion && f.Store.Count<InventoryOpening>() == 0 &&
                f.Store.Count<JournalEntry>() == 0 && f.Store.Count<StockMovement>() == 0);
        });
        test("INV06-FG07: exhausted revision preserves returned reversal reason and state", async f =>
        {
            var request = await Setup(f);
            var now = DateTimeOffset.UtcNow;
            var opening = DirectOpening(f, request, Writer.Subject, now);
            opening.SetReversalSource(Guid.NewGuid(), "سبب أولي");
            opening.Transition("submit", Writer.Subject, now);
            opening.Transition("return", "reviewer", now);
            typeof(InventoryOpening).GetProperty(nameof(InventoryOpening.Revision))!
                .SetValue(opening, int.MaxValue);
            var initialVersion = opening.Version;
            var reason = opening.ReversalReason;
            await Error("opening_revision_exhausted", () =>
            { opening.CorrectReversal("سبب آخر", now); return Task.CompletedTask; });
            Ensure(opening.State == InventoryOpeningState.Returned && opening.ReversalReason == reason &&
                opening.Version == initialVersion);
        });
        test("INV06-FG05: opening line constructor enforces positive order and parent identity", async f =>
        {
            await Setup(f);
            var item = f.Store.All<Item>().Single();
            var unit = f.Store.All<UnitOfMeasure>().Single(x => x.Id == item.BaseUnitId);
            var openingId = Guid.NewGuid();
            var valid = new InventoryOpeningLine(f.CompanyId, openingId, 1, item, unit,
                null, 2m, 1m, 2m, 5.25m, 2);
            Ensure(valid.OpeningId == openingId && valid.Position == 1 && valid.Value == 10.5m);
            await Error("invalid_opening_position", () => Task.FromResult(
                new InventoryOpeningLine(f.CompanyId, openingId, 0, item, unit, null, 2m, 1m, 2m, 5.25m, 2)));
            await Error("invalid_opening_position", () => Task.FromResult(
                new InventoryOpeningLine(f.CompanyId, openingId, -1, item, unit, null, 2m, 1m, 2m, 5.25m, 2)));
            await Error("opening_line_scope", () => Task.FromResult(
                new InventoryOpeningLine(f.CompanyId, Guid.Empty, 1, item, unit, null, 2m, 1m, 2m, 5.25m, 2)));
            Ensure(f.Store.Count<InventoryOpening>() == 0 && f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<JournalEntry>() == 0 && f.Store.Count<OutboxEvent>() == 0);
        });
        test("INV06-FG01: generated opening header preserves tenant branch and derived year", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request);
            var saved = await Get(f, created.ResourceId);
            var period = f.Store.All<FiscalPeriod>().Single(x => x.Id == saved.FiscalPeriodId);
            Ensure(saved.Id == created.ResourceId && saved.Id != Guid.Empty &&
                saved.CompanyId == f.CompanyId && saved.BranchId == request.BranchId &&
                saved.FiscalYearId == period.FiscalYearId && saved.FiscalYearId != Guid.Empty);
            await Error("opening_warehouse_mismatch", () => Create(f, request with
                { BranchId = f.OtherBranchId, CountReference = "OTHER-BRANCH" }));
            Ensure(f.Store.Count<InventoryOpening>() == 1 &&
                f.Store.Count<JournalEntry>() == 0 && f.Store.Count<StockMovement>() == 0);
        });
        test("INV06-FG02: document number, source/effect dates and functional currency are persisted", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request);
            var saved = await Get(f, created.ResourceId);
            Ensure(saved.Number == "IO-2026-000001" && saved.DocumentDate == request.DocumentDate &&
                saved.AccountingDate == request.AccountingDate && saved.CurrencyCode == "SAR" &&
                saved.FiscalPeriodId != Guid.Empty && saved.State == InventoryOpeningState.Draft);
            await Error("opening_dates_required", () => Create(f, request with
                { DocumentDate = DateOnly.MinValue, CountReference = "BAD-DATE" }));
            await Error("opening_functional_currency_only", () => Create(f, request with
                { CurrencyCode = "USD", CountReference = "FOREIGN" }));
            Ensure(f.Store.Count<InventoryOpening>() == 1);
        });
        test("INV06-FG04: cutoff date warehouse and count reference remain exact and required", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request with { CountReference = "  COUNT-REF-25 " });
            var saved = await Get(f, created.ResourceId);
            Ensure(saved.CutoffDate == request.CutoffDate &&
                saved.WarehouseId == request.WarehouseId && saved.CountReference == "COUNT-REF-25");
            await Error("invalid_text", () => Create(f, request with { CountReference = " " }));
            await Error("opening_dates_required", () => Create(f, request with
                { CutoffDate = DateOnly.MinValue, CountReference = "CUT-INVALID" }));
            await Error("reference_not_found", () => Create(f, request with
                { WarehouseId = Guid.NewGuid(), CountReference = "WH-MISSING" }));
            Ensure(f.Store.Count<InventoryOpening>() == 1 && f.Store.Count<JournalEntry>() == 0);
        });
        test("INV06-FG07: creation actor/timestamps and revision audit survive stale rejection", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request);
            var saved = await Get(f, created.ResourceId);
            Ensure(saved.CreatedBy == Writer.Subject && saved.CreatedAt != default &&
                saved.ModifiedAt >= saved.CreatedAt && saved.Revision == 1);
            var initial = await f.Service.InventoryOpening.AuditEvents(f.CompanyId, saved.Id, f.Owner, 0, 10, default);
            Ensure(initial.TotalCount == 1 && initial.Items[0].ActorId == Writer.Subject &&
                initial.Items[0].Action == "inventory-opening.created" && initial.Items[0].OccurredAt != default);
            await Error("version_conflict", () => f.Service.InventoryOpening.Edit(f.CompanyId, saved.Id, Writer,
                Fixture.Key(), new(Guid.NewGuid(), "رفض", request.Lines, "نسخة قديمة"), default));
            Ensure((await f.Service.InventoryOpening.AuditEvents(f.CompanyId, saved.Id, f.Owner, 0, 10, default)).TotalCount == 1);
            var edited = await f.Service.InventoryOpening.Edit(f.CompanyId, saved.Id, Writer,
                Fixture.Key(), new(saved.Version, "قيد افتتاح محدث", request.Lines, "تصحيح"), default);
            saved = await Get(f, saved.Id);
            var after = await f.Service.InventoryOpening.AuditEvents(f.CompanyId, saved.Id, f.Owner, 0, 10, default);
            Ensure(saved.CreatedBy == Writer.Subject && saved.Revision == 2 &&
                edited.Version == saved.Version && saved.ModifiedAt >= saved.CreatedAt &&
                after.TotalCount == 2 && after.Items.Any(x => x.Action == "inventory-opening.edited" &&
                    x.ActorId == Writer.Subject && x.OccurredAt != default));
            await Error("permission_denied", () => f.Service.InventoryOpening.AuditEvents(f.CompanyId, saved.Id,
                f.Limited, 0, 10, default));
        });
        test("INV06-OP01: create and scoped search expose stable draft pages without ledger effect", async f =>
        {
            var request = await Setup(f);
            var first = await Create(f, request with { CountReference = "LIST-01" });
            var second = await Create(f, request with { CountReference = "LIST-02" });
            var a = await f.Service.InventoryOpening.Search(f.CompanyId, Writer, request.BranchId,
                InventoryOpeningState.Draft, "LIST-", 0, 1, default);
            var b = await f.Service.InventoryOpening.Search(f.CompanyId, Writer, request.BranchId,
                InventoryOpeningState.Draft, "LIST-", 1, 1, default);
            Ensure(a.TotalCount == 2 && b.TotalCount == 2 && a.Items.Single().Id == first.ResourceId &&
                b.Items.Single().Id == second.ResourceId &&
                a.Items[0].Number != b.Items[0].Number);
            Ensure((await f.Service.InventoryOpening.Search(f.CompanyId, Writer, request.BranchId,
                InventoryOpeningState.Approved, "LIST-", 0, 10, default)).TotalCount == 0);
            await Error("invalid_pagination", () => f.Service.InventoryOpening.Search(f.CompanyId, Writer,
                request.BranchId, null, null, -1, 10, default));
            Ensure(f.Store.Count<StockMovement>() == 0 && f.Store.Count<JournalEntry>() == 0);
        });
        test("INV06-OP02: draft to submitted is idempotent and rejected stale edit is side-effect-free", async f =>
        {
            var request = await Setup(f);
            var created = await Create(f, request);
            var key = Fixture.Key();
            var submit = new TransitionRequest(created.Version);
            var submitted = await f.Service.InventoryOpening.Transition(f.CompanyId, created.ResourceId,
                Writer, key, submit, "submit", default);
            Ensure(submitted == await f.Service.InventoryOpening.Transition(f.CompanyId, created.ResourceId,
                Writer, key, submit, "submit", default));
            await Error("version_conflict", () => f.Service.InventoryOpening.Transition(f.CompanyId,
                created.ResourceId, Writer, Fixture.Key(), submit, "submit", default));
            var saved = await Get(f, created.ResourceId);
            Ensure(saved.State == InventoryOpeningState.Submitted && saved.Revision == 2 &&
                (await f.Service.InventoryOpening.AuditEvents(f.CompanyId, saved.Id,
                    f.Owner, 0, 10, default)).TotalCount == 2 &&
                f.Store.Count<JournalEntry>() == 0 && f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0);
        });
        test("INV06: draft snapshots numbering idempotency and valuation without ledger effect", async f =>
        {
            var request = await Setup(f); var key = Fixture.Key(); var r = await Create(f, request, key);
            Ensure(r == await Create(f, request, key)); var document = await Get(f, r.ResourceId);
            Ensure(document.Number == "IO-2026-000001" && document.Lines.Single().BaseQuantity == 2 && document.Revision == 1);
            var receipts = f.Store.Count<IdempotencyRecord>(); var audit = f.Store.Count<AuditEvent>();
            var preview = await f.Service.InventoryOpening.Preview(f.CompanyId, r.ResourceId, f.Owner, default);
            Ensure(preview.TotalValue == 10.5m && !preview.CanPost && preview.ExpectedVersion == r.Version);
            Ensure(f.Store.Count<JournalEntry>() == 0 && f.Store.Count<OutboxEvent>() == 0 && receipts == f.Store.Count<IdempotencyRecord>() && audit == f.Store.Count<AuditEvent>());
        });
        test("INV06: returned state requires correction before resubmit and approval locks edits", async f =>
        {
            var request = await Setup(f); var reviewer = await Reviewer(f); var r = await Action(f, await Create(f, request), "submit");
            r = await Action(f, r, "return", reviewer); Ensure((await Get(f, r.ResourceId)).State == InventoryOpeningState.Returned);
            await Error("opening_state_conflict", () => Action(f, r, "submit"));
            r = await f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, Fixture.Key(), new(r.Version, "تصحيح", request.Lines, "سبب"), default);
            r = await Action(f, r, "submit"); r = await Action(f, r, "approve", reviewer);
            Ensure((await Get(f, r.ResourceId)).ApprovedBy == reviewer.Subject);
            await Error("opening_state_conflict", () => f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, Fixture.Key(), new(r.Version, "تعديل", request.Lines, "سبب"), default));
            Ensure((await f.Service.InventoryOpening.AuditEvents(f.CompanyId, r.ResourceId, f.Owner, 0, 20, default)).TotalCount == 6);
        });
        test("INV06: self approval forbidden and posting unavailable", async f =>
        {
            var r = await Action(f, await Create(f, await Setup(f)), "submit");
            await Error("self_approval_forbidden", () => Action(f, r, "approve"));
            await Error("invalid_transition", () => Action(f, r, "post"));
            Ensure((await Get(f, r.ResourceId)).State == InventoryOpeningState.Submitted);
        });
        test("INV06: journal write permission does not confer inventory write", async f =>
        {
            var request = await Setup(f);
            await Error("permission_denied", () => f.Service.InventoryOpening.Create(f.CompanyId, f.Clerk, Fixture.Key(), request, default));
            Ensure(f.Store.Count<InventoryOpening>() == 0);
        });
        test("INV06: branch warehouse consistency and branch-scoped reads", async f =>
        {
            var request = await Setup(f);
            await Error("opening_warehouse_mismatch", () => Create(f, request with { BranchId = f.OtherBranchId }));
            var actor = new Actor("other-stock-reader");
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(actor.Subject, new[] { Permissions.Read }, false, new[] { f.OtherBranchId }, null), default);
            var r = await Create(f, request);
            Ensure((await f.Service.InventoryOpening.Search(f.CompanyId, actor, null, null, null, 0, 10, default)).TotalCount == 0);
            await Error("branch_access_denied", () => f.Service.InventoryOpening.Get(f.CompanyId, r.ResourceId, actor, default));
        });
        test("INV06: positive quantity cost bounds and item unit validation", async f =>
        {
            var request = await Setup(f); var line = request.Lines[0];
            await Error("invalid_opening_quantity", () => Create(f, request with { Lines = new[] { line with { Quantity = 0 } } }));
            await Error("invalid_opening_cost", () => Create(f, request with { Lines = new[] { line with { UnitCost = -1 } } }));
            await Error("quantity_precision", () => Create(f, request with { Lines = new[] { line with { Quantity = 0.5m } } }));
            await Error("item_unit_not_found", () => Create(f, request with { Lines = new[] { line with { UnitId = Guid.NewGuid() } } }));
            await Error("invalid_opening_lines", () => Create(f, request with { Lines = Array.Empty<InventoryOpeningLineRequest>() }));
        });
        test("INV06: alternate unit conversion snapshot and currency-scale valuation", async f =>
        {
            var request = await Setup(f); var item = await f.Service.Item.GetItem(f.CompanyId, request.Lines[0].ItemId, f.Owner, default);
            var box = item.Units.Single(x => x.FactorToBase == 12);
            var r = await Create(f, request with { Lines = new[] { request.Lines[0] with { UnitId = box.UnitId, Quantity = 0.5m, UnitCost = 1.01m } } });
            var line = (await Get(f, r.ResourceId)).Lines.Single(); Ensure(line.BaseQuantity == 6 && line.Value == 0.51m && line.FactorToBase == 12);
        });
        test("INV06: tracked lot required and wrong tracking identity rejected", async f =>
        {
            var request = await Setup(f, ItemTracking.Lot);
            await Error("opening_identity_required", () => Create(f, request with { Lines = new[] { request.Lines[0] with { IdentityId = null } } }));
            await Error("reference_not_found", () => Create(f, request with { Lines = new[] { request.Lines[0] with { IdentityId = Guid.NewGuid() } } }));
            var r = await Create(f, request); Ensure((await Get(f, r.ResourceId)).Lines.Single().LotNumber == "ID-1");
        });
        test("INV06: serial quantity exactly one and no duplicate serial in document", async f =>
        {
            var request = await Setup(f, ItemTracking.Serial); var line = request.Lines[0];
            await Error("opening_serial_quantity", () => Create(f, request with { Lines = new[] { line with { Quantity = 2 } } }));
            await Error("opening_duplicate_serial", () => Create(f, request with { Lines = new[] { line, line } }));
            var r = await Create(f, request); Ensure((await Get(f, r.ResourceId)).Lines.Single().SerialNumber == "ID-1");
        });
        test("INV06: reference change requires refreshing draft before submission", async f =>
        {
            var request = await Setup(f); var r = await Create(f, request);
            var item = await f.Service.Item.GetItem(f.CompanyId, request.Lines[0].ItemId, f.Owner, default);
            await f.Service.Item.EditItem(f.CompanyId, item.Id, f.Owner, Fixture.Key(), ItemTests.Edit(item) with { Name = "معدل" }, default);
            await Error("opening_reference_changed", () => Action(f, r, "submit"));
            r = await f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, Fixture.Key(), new(r.Version, request.Description, request.Lines, "تحديث"), default);
            await Action(f, r, "submit");
        });
        test("INV06: atomic edit rollback stale version and request replay", async f =>
        {
            var request = await Setup(f); var r = await Create(f, request); var edit = new EditInventoryOpeningRequest(r.Version, "معدل", new[] { request.Lines[0] with { Quantity = 3 } }, "سبب"); var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, key, edit, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure((await Get(f, r.ResourceId)).Lines.Single().Quantity == 2);
            var changed = await f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, key, edit, default);
            Ensure(changed == await f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, key, edit, default));
            await Error("version_conflict", () => f.Service.InventoryOpening.Edit(f.CompanyId, r.ResourceId, Writer, Fixture.Key(), edit, default));
        });
        test("INV06: pending opening blocks period close and cancellation releases blocker", async f =>
        {
            var r = await Create(f, await Setup(f)); var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Month == Fixture.Date.Month);
            var preview = await f.Service.PeriodClose.PreviewPeriodClose(f.CompanyId, period.Id, f.Approver, 0, 10, default);
            Ensure(!preview.CanCloseGeneralLedger && preview.PendingInventoryOpeningCount == 1);
            await Error("pending_inventory_openings", () => f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default));
            await Action(f, r, "cancel");
            await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default);
        });
        test("INV06: foreign currency rate and missing reference cannot create", async f =>
        {
            var request = await Setup(f);
            await Error("opening_functional_currency_only", () => Create(f, request with { CurrencyCode = "USD" }));
            await Error("opening_functional_currency_only", () => Create(f, request with { ExchangeRate = 2 }));
            await Error("reference_not_found", () => Create(f, request with { WarehouseId = Guid.NewGuid() }));
            Ensure(f.Store.Count<InventoryOpening>() == 0);
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Inventory opening assertion failed"); }
    private static async Task Error(string code, Func<Task> work)
    { try { await work(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
