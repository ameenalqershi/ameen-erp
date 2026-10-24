using System.Text.Json;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class InventoryOpeningService
{
    private async Task<(InventoryOpening Source, JournalEntry Journal, IReadOnlyList<StockMovement> Movements)> ReversibleSource(Guid cid, Guid id, CancellationToken ct)
    {
        var source = await Required<InventoryOpening>(cid, id, ct);
        Check.That(source.State == InventoryOpeningState.Posted && source.ReversesOpeningId is null && source.JournalId is not null,
            "opening_not_reversible", "العكس يتطلب افتتاحًا أصليًا مرحلًا.", ErrorKind.Conflict);
        var journal = await Required<JournalEntry>(cid, source.JournalId!.Value, ct);
        Check.That(journal.State == JournalState.Posted && journal.ReversalJournalId is null && journal.PendingReversalJournalId is null,
            "opening_already_reversed", "يوجد عكس أو طلب عكس مرتبط بالقيد.", ErrorKind.Conflict);
        var movements = await store.Find<StockMovement>(cid, x => x.OpeningId == source.Id, ct);
        Check.That(InventoryOpeningReconciliation.Matches(source, journal, movements),
            "inventory_opening_reconciliation_failed", "حركة الافتتاح لا تطابق القيد؛ صحح المطابقة قبل العكس.", ErrorKind.Conflict);
        var ids = movements.Select(x => x.Id).ToArray();
        Check.That(!(await store.Find<StockMovement>(cid, x => x.ReversesMovementId != null && ids.Contains(x.ReversesMovementId.Value), ct)).Any(),
            "opening_already_reversed", "الحركات معكوسة بالفعل.", ErrorKind.Conflict);
        return (source, journal, movements);
    }
    private async Task ValidateReversal(InventoryOpening reversal, CancellationToken ct)
    {
        Check.That(reversal.ReversesOpeningId is not null, "opening_reversal_required", "المستند ليس عكسًا.");
        var (source, _, _) = await ReversibleSource(reversal.CompanyId, reversal.ReversesOpeningId!.Value, ct);
        Check.That(reversal.AccountingDate >= source.AccountingDate, "reversal_before_original", "العكس لا يسبق تاريخ الأصل.");
        var period = await journals.OpenPeriod(reversal.CompanyId, reversal.AccountingDate, ct);
        Check.That(period.Id == reversal.FiscalPeriodId, "period_mismatch", "فترة العكس غير مطابقة.");
        // Copy historical source dimensions/amounts even when master cards or posting settings have changed.
        Check.That(reversal.BranchId == source.BranchId && reversal.WarehouseId == source.WarehouseId && reversal.CurrencyCode == source.CurrencyCode &&
            reversal.Lines.Count == source.Lines.Count && reversal.Lines.All(x => source.Lines.Any(y => y.Position == x.Position &&
                y.ItemId == x.ItemId && y.UnitId == x.UnitId && y.IdentityId == x.IdentityId && y.BaseQuantity == x.BaseQuantity &&
                y.Quantity == x.Quantity && y.FactorToBase == x.FactorToBase && y.UnitCost == x.UnitCost && y.Value == x.Value)),
            "opening_reversal_snapshot_mismatch", "العكس لا يطابق لقطة الأصل.", ErrorKind.Conflict);
    }
    public async Task<Page<InventoryOpening>> Reversals(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeOpening(cid, id, actor, Permissions.Read, ct);
        return await store.ReadPage<InventoryOpening>(cid, x => x.ReversesOpeningId == id,
            q => q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public Task<CommandReceipt> CreateReversal(Guid cid, Guid id, Actor actor, string key, ReverseInventoryOpeningRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.reverse:" + id, request,
            token => AuthorizeOpening(cid, id, actor, Permissions.InventoryReverse, token), async token =>
        {
            var (source, _, _) = await ReversibleSource(cid, id, token); Check.Version(source, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب العكس", 500);
            Check.That(request.AccountingDate >= source.AccountingDate, "reversal_before_original", "العكس لا يسبق تاريخ الأصل.");
            Check.That(!(await store.Find<InventoryOpening>(cid, x => x.ReversesOpeningId == id && x.State != InventoryOpeningState.Cancelled, token)).Any(),
                "opening_reversal_exists", "يوجد مستند عكس غير ملغى.", ErrorKind.Conflict);
            var period = await journals.OpenPeriod(cid, request.AccountingDate, token);
            var year = await Required<FiscalYear>(cid, period.FiscalYearId, token); var scope = "IOR:" + year.Id;
            var sequence = (await store.Find<NumberSequence>(cid, x => x.Scope == scope, token)).SingleOrDefault();
            if (sequence is null) { sequence = new NumberSequence(cid, scope); store.Add(sequence); }
            var reversal = new InventoryOpening(cid, source.BranchId, source.WarehouseId, period, $"IOR-{year.Code}-{sequence.Next():000000}",
                request.AccountingDate, request.AccountingDate, source.CutoffDate, source.CurrencyCode, source.CountReference,
                "عكس " + source.Number, source.Number, actor.Subject, clock.UtcNow);
            reversal.Edit(reversal.Description, source.Lines.Select(x => x.CopyFor(reversal.Id)), clock.UtcNow);
            reversal.SetReversalSource(source.Id, reason); store.Add(reversal);
            Audit(cid, actor, "inventory-opening.reversal-created", reversal, new { SourceId = source.Id, reason, reversal });
            return Receipt(reversal, reversal.State.ToString(), reversal.Number);
        }, ct);
    public Task<CommandReceipt> CorrectReversal(Guid cid, Guid id, Actor actor, string key, TransitionRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.correct-reversal:" + id, request,
            token => AuthorizeOpening(cid, id, actor, Permissions.InventoryReverse, token), async token =>
        {
            var reversal = await Required<InventoryOpening>(cid, id, token); Check.Version(reversal, request.ExpectedVersion);
            await ValidateReversal(reversal, token); reversal.CorrectReversal(request.Reason!, clock.UtcNow);
            Audit(cid, actor, "inventory-opening.reversal-corrected", reversal, new { reversal.ReversalReason, reversal.Revision });
            return Receipt(reversal, reversal.State.ToString(), reversal.Number);
        }, ct);
    public Task<CommandReceipt> PostReversal(Guid cid, Guid id, Actor actor, string key, TransitionRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "inventory-opening.post-reversal:" + id, request,
            async token => { await AuthorizeOpening(cid, id, actor, Permissions.InventoryPost, token); await AuthorizeOpening(cid, id, actor, Permissions.InventoryReverse, token); }, async token =>
        {
            var reversal = await Required<InventoryOpening>(cid, id, token); Check.Version(reversal, request.ExpectedVersion);
            Check.That(reversal.State == InventoryOpeningState.Approved, "opening_state_conflict", "العكس غير معتمد.", ErrorKind.Conflict);
            await ValidateReversal(reversal, token);
            var (source, originalJournal, movements) = await ReversibleSource(cid, reversal.ReversesOpeningId!.Value, token);
            var number = await journals.NextNumber(cid, reversal.FiscalPeriodId, token);
            var journal = new JournalEntry(cid, reversal.BranchId, reversal.FiscalPeriodId, number.Number, number.Sequence,
                reversal.DocumentDate, reversal.AccountingDate, reversal.Description, reversal.Number, reversal.CreatedBy, reversal.CreatedAt);
            journal.SetInventoryOpeningSource(reversal.Id, originalJournal.Id); journal.SetLines(originalJournal.Lines.Select(x => x.Opposite(journal.Id)));
            journal.Submit(); journal.Approve(reversal.ApprovedBy!, true); journal.Post(clock.UtcNow); store.Add(journal);
            originalJournal.ReserveReversal(journal.Id); originalJournal.LinkReversal(journal.Id);
            foreach (var movement in movements) store.Add(movement.Opposite(reversal, journal.Id, clock.UtcNow));
            reversal.Post(journal.Id, source.InventoryAccountId!.Value, source.ClearingAccountId!.Value, source.PostingSettingsVersion!.Value, actor.Subject, clock.UtcNow);
            Audit(cid, actor, "inventory-opening.reversal-posted", reversal, new { reversal.ReversesOpeningId, reversal.JournalId, reversal.ReversalReason });
            Audit(cid, actor, "journal.reversed", originalJournal, new { ReversalId = journal.Id, reversal.Id });
            Audit(cid, actor, "journal.post", journal, new { journal.InventoryOpeningId, journal.ReversesJournalId, journal.Lines });
            store.Add(new OutboxEvent(cid, "ledger.journal-posted.v1", journal.Id, clock.UtcNow, JsonSerializer.Serialize(new { journal.Id, journal.Number, journal.InventoryOpeningId, journal.ReversesJournalId }, JsonDefaults.Options)));
            store.Add(new OutboxEvent(cid, "stock.inventory-opening-reversed.v1", reversal.Id, clock.UtcNow, JsonSerializer.Serialize(new { reversal.Id, reversal.ReversesOpeningId, reversal.JournalId }, JsonDefaults.Options)));
            return Receipt(reversal, reversal.State.ToString(), journal.Number);
        }, ct);
}
