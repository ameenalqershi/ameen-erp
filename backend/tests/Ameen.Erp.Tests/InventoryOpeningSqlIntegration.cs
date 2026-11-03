using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class InventoryOpeningSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Guid branch, Actor owner, Actor writer, Actor reviewer)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        var sales = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-S", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default));
        var stock = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-I", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default));
        var cost = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-C", "تكلفة", AccountType.Expense, null, true, false, false, null, NormalBalance.Debit), default));
        var group = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(), new("OPEN-G", "مجموعة", sales.ResourceId, InventoryAccountId: stock.ResourceId, CostAccountId: cost.ResourceId), default));
        var unit = await Call(s => s.Unit.CreateUnit(cid, owner, Fixture.Key(), new("OPEN-U", "وحدة", "EA", 0, "وحدة"), default));
        var item = await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(), new("OPEN-ITEM", "صنف", ItemType.Goods, ItemTracking.None, group.ResourceId, unit.ResourceId, new[] { new ItemUnitRequest(unit.ResourceId, 1, null, null) }), default));
        Guid keeper; await using (var db = Context()) keeper = (await db.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == owner.Subject)).Id;
        var wh = await Call(s => s.Warehouse.CreateWarehouse(cid, owner, Fixture.Key(), new("OPEN-WH", "مخزن", branch, keeper, stock.ResourceId, WarehouseType.Standard, false, Array.Empty<WarehouseLocationRequest>()), default));
        var request = new CreateInventoryOpeningRequest(branch, wh.ResourceId, Fixture.Date, Fixture.Date, Fixture.Date, "SAR", 1, "SQL-COUNT", "افتتاح", new[] { new InventoryOpeningLineRequest(item.ResourceId, unit.ResourceId, 2, 5) });
        var key = Fixture.Key(); var replay = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.InventoryOpening.Create(cid, writer, key, request, default))));
        Ensure(replay.Distinct().Count() == 1, "Opening concurrent replay differs"); var r = replay[0];
        Ensure((await Call(s => s.InventoryOpening.Preview(cid, r.ResourceId, writer, default))).TotalValue == 10, "SQL valuation differs");
        var more = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Call(s => s.InventoryOpening.Create(cid, writer, Fixture.Key(), request, default))));
        Ensure(more.Select(x => x.Number).Append(r.Number).Distinct().Count() == 4, "Opening numbers duplicated");
        var edit = new EditInventoryOpeningRequest(r.Version, "معدل", new[] { request.Lines[0] with { Quantity = 3 } }, "تصحيح"); key = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_OpeningAudit] CHECK ([Action] <> 'inventory-opening.edited')");
        try { await Call(s => s.InventoryOpening.Edit(cid, r.ResourceId, writer, key, edit, default)); throw new Exception("Expected audit failure"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_OpeningAudit]");
            var saved = await db.Set<InventoryOpening>().SingleAsync(x => x.CompanyId == cid && x.Id == r.ResourceId);
            Ensure(saved.Version == r.Version && saved.Lines.Single().Quantity == 2, "Opening edit rollback failed");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed receipt persisted");
        }
        await using (var db = Context())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [opening_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [opening_runtime_test]; EXECUTE AS USER = 'opening_runtime_test';");
            try { var s = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()); r = await s.InventoryOpening.Edit(cid, r.ResourceId, writer, key, edit, default); }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [opening_runtime_test];"); }
        }
        Ensure((await Call(s => s.InventoryOpening.Get(cid, r.ResourceId, writer, default))).Lines.Single().Quantity == 3, "SQL line replacement failed");
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, writer, Fixture.Key(), new(r.Version), "submit", default));
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, reviewer, Fixture.Key(), new(r.Version, "تصحيح"), "return", default));
        r = await Call(s => s.InventoryOpening.Edit(cid, r.ResourceId, writer, Fixture.Key(), edit with { ExpectedVersion = r.Version }, default));
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, writer, Fixture.Key(), new(r.Version), "submit", default));
        r = await Call(s => s.InventoryOpening.Transition(cid, r.ResourceId, reviewer, Fixture.Key(), new(r.Version), "approve", default));
        await using (var db = Context())
        {
            var saved = await db.Set<InventoryOpening>().SingleAsync(x => x.CompanyId == cid && x.Id == r.ResourceId);
            Ensure(saved.State == InventoryOpeningState.Approved && saved.ApprovedBy == reviewer.Subject, "SQL approval failed");
            db.Remove(saved.Lines.Single());
            try { await db.SaveChangesAsync(); throw new Exception("Approved line deletion accepted"); }
            catch (BusinessException e) when (e.Code == "opening_lines_immutable") { }
        }
        var periodId = (await Call(s => s.InventoryOpening.Get(cid, r.ResourceId, writer, default))).FiscalPeriodId;
        var close = await Call(s => s.PeriodClose.PreviewPeriodClose(cid, periodId, reviewer, 0, 10, default));
        Ensure(close.PendingInventoryOpeningCount >= 4 && !close.CanCloseGeneralLedger, "Pending openings omitted from close checks");
        Ensure((await Call(s => s.InventoryOpening.Search(cid, writer, branch, InventoryOpeningState.Approved, "SQL-COUNT", 0, 10, default))).TotalCount == 1, "SQL opening search failed");
        int beforePrintAudit, beforePrintReceipts, beforePrintOutbox;
        long beforePrintNumbers;
        await using (var db = Context())
        {
            beforePrintAudit = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            beforePrintReceipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            beforePrintOutbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            beforePrintNumbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).SumAsync(x => x.LastNumber);
        }
        var approvedPrint = await Call(s => s.InventoryOpening.Print(cid, r.ResourceId, writer, default));
        Ensure(approvedPrint.OpeningId == r.ResourceId && approvedPrint.Number == r.Number &&
            approvedPrint.CurrencyCode == "SAR" && approvedPrint.ExchangeRate == 1m &&
            approvedPrint.State == nameof(InventoryOpeningState.Approved) &&
            approvedPrint.JournalId == null && approvedPrint.Lines.Count == 1 &&
            approvedPrint.Lines[0].ItemCode == "OPEN-ITEM" &&
            approvedPrint.Lines[0].UnitCode == "OPEN-U" &&
            approvedPrint.Lines[0].Quantity == 3m &&
            approvedPrint.TotalDocumentValue == 15m &&
            approvedPrint.SignedEffectValue == 15m,
            "INV06 SQL print did not preserve approved opening snapshot");
        try
        {
            await Call(s => s.InventoryOpening.Print(cid, r.ResourceId,
                new Actor("inv06-print-nonmember"), default));
            throw new Exception("INV06 SQL print exposed company to outsider");
        }
        catch (BusinessException e) when (e.Code == "permission_denied") { }
        await using (var db = Context())
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == beforePrintAudit &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == beforePrintReceipts &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == beforePrintOutbox &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).SumAsync(x => x.LastNumber) == beforePrintNumbers,
                "INV06 SQL print changed audit/receipts/outbox/number allocation");
        Console.WriteLine("  SQL PASS INV06 concurrent replay/numbering, valuation, audit rollback, runtime line replacement, return/correct/approve, immutable lines and close blockers");
        var clearing = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("OPEN-CLEAR", "تسوية افتتاح", AccountType.Equity, null, true, false, false, null, NormalBalance.Credit), default));
        var settings = await Call(s => s.InventoryOpening.GetPostingSettings(cid, owner, default));
        await Call(s => s.InventoryOpening.ConfigurePosting(cid, owner, Fixture.Key(), new(settings?.Version, clearing.ResourceId, "إعداد"), default));
        var preview = await Call(s => s.InventoryOpening.Preview(cid, r.ResourceId, writer, default));
        Ensure(preview.CanPost && preview.PostingPlan is not null, "SQL posting plan unavailable");
        var post = new PostInventoryOpeningRequest(r.Version, preview.PostingPlan!.SettingsVersion, preview.PostingPlan.WarehouseVersion);
        var postKey = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_StockPost] CHECK ([Type] <> 'stock.inventory-opening-posted.v1')");
        try { await Call(s => s.InventoryOpening.Post(cid, r.ResourceId, writer, postKey, post, default)); throw new Exception("Posting failure expected"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] DROP CONSTRAINT [CK_Test_StockPost]");
            Ensure(!await db.Set<StockMovement>().AnyAsync(x => x.OpeningId == r.ResourceId) && !await db.Set<JournalEntry>().AnyAsync(x => x.InventoryOpeningId == r.ResourceId), "Failed posting leaked ledger rows");
            Ensure((await db.Set<InventoryOpening>().SingleAsync(x => x.Id == r.ResourceId)).State == InventoryOpeningState.Approved, "Failed posting changed source");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == postKey), "Failed posting saved receipt");
        }
        await using (var db = Context())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [posting_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [posting_runtime_test]; EXECUTE AS USER = 'posting_runtime_test';");
            try
            {
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                await service.InventoryOpening.Post(cid, r.ResourceId, writer, postKey, post, default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [posting_runtime_test];"); }
        }
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.InventoryOpening.Post(cid, r.ResourceId, writer, postKey, post, default))));
        Ensure(posts.Distinct().Count() == 1, "Concurrent post receipts differ");
        Guid postedOpeningJournalId;
        Guid postedOpeningJournalVersion;
        await using (var db = Context())
        {
            var document = await db.Set<InventoryOpening>().SingleAsync(x => x.Id == r.ResourceId);
            var journal = await db.Set<JournalEntry>().SingleAsync(x => x.InventoryOpeningId == document.Id);
            postedOpeningJournalId = journal.Id;
            postedOpeningJournalVersion = journal.Version;
            var movement = await db.Set<StockMovement>().SingleAsync(x => x.OpeningId == document.Id);
            Ensure(document.JournalId == journal.Id && document.State == InventoryOpeningState.Posted && journal.State == JournalState.Posted &&
                movement.Value == 15 && movement.BaseQuantity == 3 && journal.Lines.Sum(x => x.FunctionalDebit) == 15 && journal.Lines.Sum(x => x.FunctionalCredit) == 15,
                "Stock and control journal do not reconcile");
            Ensure(await db.Set<OutboxEvent>().CountAsync(x => x.ResourceId == document.Id && x.Type == "stock.inventory-opening-posted.v1") == 1, "Duplicate stock outbox");
            db.Remove(movement);
            try { await db.SaveChangesAsync(); throw new Exception("Stock delete accepted"); }
            catch (BusinessException e) when (e.Code == "stock_movement_immutable") { }
        }
        var itemUsagePosted = await Call(s => s.Item.GetItemUsage(cid, item.ResourceId, writer, default));
        var warehouseUsagePosted = await Call(s => s.Warehouse.GetWarehouseUsage(cid, wh.ResourceId, writer, default));
        Ensure(itemUsagePosted.MovementCount == 1 && itemUsagePosted.OpeningDocumentCount == 1 &&
            warehouseUsagePosted.MovementCount == 1 && warehouseUsagePosted.OpeningDocumentCount == 1 &&
            (await Call(s => s.Item.ListItemMovements(cid, item.ResourceId, writer, 0, 1, default))).TotalCount == 1,
            "SQL INV01/INV04 usage missing posted opening");
        Console.WriteLine("  SQL PASS INV01/INV04 usage references posted document and movement via scoped API");
        var postedPrint = await Call(s => s.InventoryOpening.Print(cid, r.ResourceId, writer, default));
        Ensure(postedPrint.State == nameof(InventoryOpeningState.Posted) &&
            postedPrint.JournalId == postedOpeningJournalId &&
            postedPrint.TotalDocumentValue == approvedPrint.TotalDocumentValue &&
            postedPrint.SignedEffectValue == approvedPrint.SignedEffectValue &&
            postedPrint.PostedBy is not null && postedPrint.PostedAt.HasValue,
            "INV06 SQL printed posted document lost linked journal or repriced lines");
        // GL07-C01: source-owned INV06 journal must be reversed only through INV06,
        // even though it is balanced and posted. A rejected GL request is atomic.
        int gl07Journals, gl07Audits, gl07Receipts, gl07Outbox;
        long gl07Numbers;
        await using (var db = Context())
        {
            gl07Journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            gl07Audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            gl07Receipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            gl07Outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            gl07Numbers = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).SumAsync(x => x.LastNumber);
        }
        var rejectedGlKey = Fixture.Key();
        try
        {
            await Call(s => s.Journal.ReverseJournal(cid, postedOpeningJournalId, writer,
                rejectedGlKey, new(postedOpeningJournalVersion, Fixture.Date, "غير مسموح من اليومية"), default));
            throw new Exception("GL07-C01 accepted direct reversal of INV06 source journal");
        }
        catch (BusinessException e) when (e.Code == "source_reversal_required") { }
        await using (var db = Context())
        {
            Ensure(await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == gl07Journals &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == gl07Audits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == gl07Receipts &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == gl07Outbox &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).SumAsync(x => x.LastNumber) == gl07Numbers &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == rejectedGlKey),
                "GL07-C01 denied source reversal changed journal, audit, receipt, outbox or numbering");
            var originalStockJournal = await db.Set<JournalEntry>().SingleAsync(x => x.Id == postedOpeningJournalId);
            Ensure(originalStockJournal.State == JournalState.Posted &&
                originalStockJournal.ReversalJournalId == null && originalStockJournal.Version == postedOpeningJournalVersion,
                "GL07-C01 direct reversal mutated the linked stock journal");
        }
        Console.WriteLine("  SQL PASS GL07-C01 INV06 journal cannot reverse outside source workflow, no SQL side effects");
        // Different document/key cannot repeat the same warehouse/item opening.
        var duplicate = more[0];
        duplicate = await Call(s => s.InventoryOpening.Transition(cid, duplicate.ResourceId, writer, Fixture.Key(), new(duplicate.Version), "submit", default));
        duplicate = await Call(s => s.InventoryOpening.Transition(cid, duplicate.ResourceId, reviewer, Fixture.Key(), new(duplicate.Version), "approve", default));
        try { await Call(s => s.InventoryOpening.Post(cid, duplicate.ResourceId, writer, Fixture.Key(), post with { ExpectedVersion = duplicate.Version }, default)); throw new Exception("Duplicate opening accepted"); }
        catch (BusinessException e) when (e.Code == "inventory_opening_already_recorded") { }
        // Administrative corruption is detected before closing, even though the journal stays balanced.
        await using (var db = Context()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [StockMovements] SET [Value]=[Value]+1 WHERE [OpeningId]={r.ResourceId}");
        var corrupt = await Call(s => s.PeriodClose.PreviewPeriodClose(cid, periodId, reviewer, 0, 10, default));
        Ensure(!corrupt.Checks.Single(x => x.Code == "inventory_openings_reconciled").Passed, "Close ignored mismatched stock valuation");
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [StockMovements] SET [Value]=[Value]-1 WHERE [OpeningId]={r.ResourceId}");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [stock_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [stock_runtime_test]; EXECUTE AS USER = 'stock_runtime_test';");
            try
            {
                try { await db.Database.ExecuteSqlRawAsync("UPDATE [StockMovements] SET [Value]=[Value]+1"); throw new Exception("Runtime stock mutation accepted"); }
                catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 229) { }
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [stock_runtime_test];"); }
        }
        Console.WriteLine("  SQL PASS INV06 atomic posting rollback/retry, concurrent replay, duplicate opening prevention, stock/control reconciliation, immutable ledger and runtime denial");
        var stockReport = await Call(s => s.Report.InventoryBalances(cid, writer, Fixture.Date, Fixture.Date, branch, wh.ResourceId, group.ResourceId, item.ResourceId, true, 0, 1, default));
        Ensure(stockReport.Rows.TotalCount == 1 && stockReport.ClosingValue == 15 && stockReport.Rows.Items.Single().ClosingQuantity == 3, "SQL inventory balances filters/totals differ");
        var controlReport = await Call(s => s.Report.AccountLedger(cid, stock.ResourceId, writer, Fixture.Date, Fixture.Date, branch, 0, 10, default));
        Ensure(stockReport.ClosingValue == controlReport.ClosingBalance, "SQL stock report differs from control ledger");
        var postedSource = await Call(s => s.InventoryOpening.Get(cid, r.ResourceId, writer, default));
        var reverseKey = Fixture.Key(); var reverseRequest = new ReverseInventoryOpeningRequest(postedSource.Version, Fixture.Date, "تصحيح SQL");
        var reversals = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.InventoryOpening.CreateReversal(cid, r.ResourceId, writer, reverseKey, reverseRequest, default))));
        Ensure(reversals.Distinct().Count() == 1, "Reversal draft replay duplicated");
        try { await Call(s => s.InventoryOpening.CreateReversal(cid, r.ResourceId, writer, Fixture.Key(), reverseRequest, default)); throw new Exception("Second live reversal accepted"); }
        catch (BusinessException e) when (e.Code == "opening_reversal_exists") { }
        var reversal = reversals[0];
        reversal = await Call(s => s.InventoryOpening.Transition(cid, reversal.ResourceId, writer, Fixture.Key(), new(reversal.Version), "submit", default));
        reversal = await Call(s => s.InventoryOpening.Transition(cid, reversal.ResourceId, reviewer, Fixture.Key(), new(reversal.Version), "approve", default));
        var reversePost = new TransitionRequest(reversal.Version); var reversePostKey = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_ReverseStock] CHECK ([Type] <> 'stock.inventory-opening-reversed.v1')");
        try { await Call(s => s.InventoryOpening.PostReversal(cid, reversal.ResourceId, writer, reversePostKey, reversePost, default)); throw new Exception("Reverse Outbox failure expected"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] DROP CONSTRAINT [CK_Test_ReverseStock]");
            Ensure(!await db.Set<StockMovement>().AnyAsync(x => x.OpeningId == reversal.ResourceId) && !await db.Set<JournalEntry>().AnyAsync(x => x.InventoryOpeningId == reversal.ResourceId), "Failed reversal leaked effects");
            var originalJournal = await db.Set<JournalEntry>().SingleAsync(x => x.Id == postedSource.JournalId);
            Ensure(originalJournal.State == JournalState.Posted && originalJournal.ReversalJournalId == null, "Failed reversal changed original journal");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == reversePostKey), "Failed reversal saved receipt");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [reverse_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [reverse_runtime_test]; EXECUTE AS USER = 'reverse_runtime_test';");
            try
            {
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                await service.InventoryOpening.PostReversal(cid, reversal.ResourceId, writer, reversePostKey, reversePost, default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [reverse_runtime_test];"); }
        }
        var reversedReplays = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.InventoryOpening.PostReversal(cid, reversal.ResourceId, writer, reversePostKey, reversePost, default))));
        Ensure(reversedReplays.Distinct().Count() == 1, "Reversal post replay differs");
        await using (var db = Context())
        {
            var movements = await db.Set<StockMovement>().Where(x => x.OpeningId == r.ResourceId || x.OpeningId == reversal.ResourceId).ToListAsync();
            Ensure(movements.Count == 2 && movements.Sum(x => x.Value) == 0 && movements.Sum(x => x.BaseQuantity) == 0, "Stock reversal does not net to zero");
            var originalJournal = await db.Set<JournalEntry>().SingleAsync(x => x.Id == postedSource.JournalId);
            var reverseJournal = await db.Set<JournalEntry>().SingleAsync(x => x.InventoryOpeningId == reversal.ResourceId);
            Ensure(originalJournal.State == JournalState.Reversed && originalJournal.ReversalJournalId == reverseJournal.Id && reverseJournal.ReversesJournalId == originalJournal.Id, "Reverse provenance differs");
            Ensure(originalJournal.Lines.Single(x => x.Position == 1).FunctionalDebit == reverseJournal.Lines.Single(x => x.Position == 1).FunctionalCredit, "Historical journal value differs");
        }
        var itemUsageReversed = await Call(s => s.Item.GetItemUsage(cid, item.ResourceId, writer, default));
        var warehouseUsageReversed = await Call(s => s.Warehouse.GetWarehouseUsage(cid, wh.ResourceId, writer, default));
        Ensure(itemUsageReversed.MovementCount == 2 && itemUsageReversed.OpeningDocumentCount == 2 &&
            itemUsageReversed.ReversalMovementCount == 1 &&
            warehouseUsageReversed.MovementCount == 2 && warehouseUsageReversed.OpeningDocumentCount == 2 &&
            warehouseUsageReversed.ReversalMovementCount == 1 &&
            (await Call(s => s.Warehouse.ListWarehouseMovements(cid, wh.ResourceId, writer, 1, 1, default))).TotalCount == 2,
            "SQL INV01/INV04 usage lost historical original/reversal document");
        Console.WriteLine("  SQL PASS INV01/INV04 usage retains original and reversal source history, paging and ledger count");
        // INV04-GAP03: even a fully reversed quantity/value retains immutable stock provenance.
        // The stock structure remains locked; a failed edit must not persist revision, audit or receipt.
        var protectedItem = await Call(s => s.Item.GetItem(cid, item.ResourceId, owner, default));
        int priorItemAudits, priorItemReceipts;
        await using (var db = Context())
        {
            priorItemAudits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.ResourceId == item.ResourceId);
            priorItemReceipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
        }
        var forbiddenItemKey = Fixture.Key();
        try
        {
            await Call(s => s.Item.EditItem(cid, item.ResourceId, owner, forbiddenItemKey,
                ItemTests.Edit(protectedItem) with { Code = "SQL-USED-ITEM-CHANGED" }, default));
            throw new Exception("A stock-used item was edited after complete reversal");
        }
        catch (BusinessException e) when (e.Code == "item_stock_structure_in_use") { }
        await using (var db = Context())
        {
            var savedItem = await db.Set<Item>().SingleAsync(x => x.CompanyId == cid && x.Id == item.ResourceId);
            Ensure(savedItem.Version == protectedItem.Version && savedItem.Code == protectedItem.Code &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.ResourceId == item.ResourceId) == priorItemAudits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == priorItemReceipts &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == forbiddenItemKey),
                "INV04-GAP03 denied SQL item edit changed revision, audit or idempotency record");
        }
        Console.WriteLine("  SQL PASS INV04-GAP03: item identity remains immutable after full stock reversal");
        // INV01-GAP02: a real SQL-posted and fully reversed opening still protects
        // the warehouse's historical stock structure even though the net quantity is zero.
        var protectedWarehouse = await Call(s => s.Warehouse.GetWarehouse(cid, wh.ResourceId, owner, default));
        var warehouseEdit = WarehouseTests.Edit(protectedWarehouse);
        int warehouseAuditsBefore, warehouseReceiptsBefore;
        await using (var db = Context())
        {
            warehouseAuditsBefore = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.ResourceId == wh.ResourceId);
            warehouseReceiptsBefore = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
        }
        foreach (var forbiddenEdit in new[]
        {
            warehouseEdit with { Code = "SQL-HISTORIC-RECODE" },
            warehouseEdit with { Type = WarehouseType.Quarantine },
            warehouseEdit with { TrackLocations = true,
                Locations = new[] { new WarehouseLocationRequest("SQL-L01", "رف جديد") } }
        })
        {
            var deniedKey = Fixture.Key();
            try
            {
                await Call(s => s.Warehouse.EditWarehouse(cid, wh.ResourceId, owner, deniedKey, forbiddenEdit, default));
                throw new Exception("SQL warehouse historical structure modified after full reversal");
            }
            catch (BusinessException e) when (e.Code == "warehouse_stock_structure_in_use") { }
            await using var db = Context();
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == deniedKey),
                "Rejected historical warehouse edit consumed idempotency key");
        }
        await using (var db = Context())
        {
            var saved = await db.Set<Warehouse>().SingleAsync(x => x.CompanyId == cid && x.Id == wh.ResourceId);
            Ensure(saved.Version == protectedWarehouse.Version && saved.Type == protectedWarehouse.Type &&
                saved.Code == protectedWarehouse.Code && !saved.TrackLocations &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.ResourceId == wh.ResourceId) == warehouseAuditsBefore &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == warehouseReceiptsBefore,
                "INV01-GAP02 rejected warehouse SQL edit persisted version, audit or receipt");
        }
        var harmlessEdit = await Call(s => s.Warehouse.EditWarehouse(cid, wh.ResourceId, owner, Fixture.Key(),
            warehouseEdit with { Name = "وصف بعد عكس المخزون" }, default));
        Ensure((await Call(s => s.Warehouse.GetWarehouse(cid, wh.ResourceId, owner, default))).Version == harmlessEdit.Version &&
            (await Call(s => s.Warehouse.GetWarehouseUsage(cid, wh.ResourceId, owner, default))).MovementCount == 2,
            "INV01-GAP02 descriptive edit lost historical warehouse movement references");
        Console.WriteLine("  SQL PASS INV01-GAP02: reversed stock keeps warehouse structure, audits and receipts intact");

        // INV05-GAP03: verify the serial identity guard against real SQL rows, not only MemoryStore.
        // The full reversal has zero net balance, but its two movements must preserve serial provenance.
        var serialItem = await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(),
            new("OPEN-POSTED-SERIAL", "صنف تسلسلي", ItemType.Goods, ItemTracking.Serial,
                group.ResourceId, unit.ResourceId, new[] { new ItemUnitRequest(unit.ResourceId, 1m) }), default));
        var serialReceipt = await Call(s => s.InventoryIdentity.CreateSerial(cid, serialItem.ResourceId, owner, Fixture.Key(),
            new("SERIAL-HISTORY-CARD", "رقم مخزني تاريخي", null, null, InventoryQualityStatus.Available,
                SerialNumber: "SERIAL-2026-ONE"), default));
        var serialSource = request with
        {
            CountReference = "HISTORICAL-SERIAL-2026",
            Lines = new[] { new InventoryOpeningLineRequest(serialItem.ResourceId, unit.ResourceId, 1m, 7m, serialReceipt.ResourceId) }
        };
        var serialOpening = await Call(s => s.InventoryOpening.Create(cid, writer, Fixture.Key(), serialSource, default));
        serialOpening = await Call(s => s.InventoryOpening.Transition(cid, serialOpening.ResourceId, writer,
            Fixture.Key(), new(serialOpening.Version), "submit", default));
        serialOpening = await Call(s => s.InventoryOpening.Transition(cid, serialOpening.ResourceId, reviewer,
            Fixture.Key(), new(serialOpening.Version), "approve", default));
        var serialPreview = await Call(s => s.InventoryOpening.Preview(cid, serialOpening.ResourceId, writer, default));
        Ensure(serialPreview.CanPost && serialPreview.PostingPlan is not null, "Serial opening SQL plan unavailable");
        var postedSerial = await Call(s => s.InventoryOpening.Post(cid, serialOpening.ResourceId, writer, Fixture.Key(),
            new(serialOpening.Version, serialPreview.PostingPlan!.SettingsVersion, serialPreview.PostingPlan.WarehouseVersion), default));
        var serialReversal = await Call(s => s.InventoryOpening.CreateReversal(cid, postedSerial.ResourceId, writer, Fixture.Key(),
            new(postedSerial.Version, Fixture.Date, "عكس تسلسل تاريخي"), default));
        serialReversal = await Call(s => s.InventoryOpening.Transition(cid, serialReversal.ResourceId, writer,
            Fixture.Key(), new(serialReversal.Version), "submit", default));
        serialReversal = await Call(s => s.InventoryOpening.Transition(cid, serialReversal.ResourceId, reviewer,
            Fixture.Key(), new(serialReversal.Version), "approve", default));
        await Call(s => s.InventoryOpening.PostReversal(cid, serialReversal.ResourceId, writer, Fixture.Key(),
            new(serialReversal.Version), default));
        var serialIdentity = await Call(s => s.InventoryIdentity.GetSerial(cid, serialItem.ResourceId,
            serialReceipt.ResourceId, owner, default));
        var serialEdit = new EditInventoryIdentityRequest(serialIdentity.Version, serialIdentity.Code,
            serialIdentity.Name, serialIdentity.ProductionDate, serialIdentity.ExpiryDate,
            serialIdentity.QualityStatus, serialIdentity.EnglishName, serialIdentity.Notes,
            "تثبيت المرجع", SerialNumber: serialIdentity.SerialNumber);
        int serialAuditBefore, serialReceiptsBefore, serialOutboxBefore, serialJournalsBefore;
        await using (var db = Context())
        {
            var movements = await db.Set<StockMovement>().Where(x => x.CompanyId == cid && x.IdentityId == serialIdentity.Id).ToListAsync();
            Ensure(movements.Count == 2 && movements.Sum(x => x.BaseQuantity) == 0 &&
                movements.Sum(x => x.Value) == 0m && serialIdentity.SerialNumber == "SERIAL-2026-ONE",
                "SQL reversed serial lost signed historical movements");
            serialAuditBefore = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            serialReceiptsBefore = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            serialOutboxBefore = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid);
            serialJournalsBefore = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
        }
        foreach (var invalid in new[]
        {
            serialEdit with { SerialNumber = "SERIAL-RENUMBER-DENIED" },
            serialEdit with { Code = "SERIAL-CODE-DENIED" }
        })
        {
            var deniedKey = Fixture.Key();
            try
            {
                await Call(s => s.InventoryIdentity.EditSerial(cid, serialItem.ResourceId,
                    serialIdentity.Id, owner, deniedKey, invalid, default));
                throw new Exception("Historical SQL serial renumber accepted");
            }
            catch (BusinessException e) when (e.Code == "inventory_identity_in_stock") { }
            await using var db = Context();
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == deniedKey),
                "Denied serial SQL edit saved idempotency receipt");
        }
        await using (var db = Context())
        {
            var saved = await db.Set<InventoryIdentity>().SingleAsync(x => x.CompanyId == cid && x.Id == serialIdentity.Id);
            Ensure(saved.Version == serialIdentity.Version && saved.Revision == serialIdentity.Revision &&
                saved.Code == serialIdentity.Code && saved.SerialNumber == serialIdentity.SerialNumber &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == serialAuditBefore &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == serialReceiptsBefore &&
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid) == serialOutboxBefore &&
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == serialJournalsBefore,
                "Historical SQL serial rejection leaked audit, receipt, journal or outbox");
        }
        var harmlessSerial = await Call(s => s.InventoryIdentity.EditSerial(cid, serialItem.ResourceId,
            serialIdentity.Id, owner, Fixture.Key(), serialEdit with { Name = "وصف تسلسلي بعد العكس" }, default));
        var savedSerial = await Call(s => s.InventoryIdentity.GetSerial(cid, serialItem.ResourceId,
            serialIdentity.Id, owner, default));
        Ensure(savedSerial.Name == "وصف تسلسلي بعد العكس" && savedSerial.Version == harmlessSerial.Version &&
            savedSerial.SerialNumber == serialIdentity.SerialNumber,
            "Descriptive SQL serial edit after reversal changed historical number");
        Console.WriteLine("  SQL PASS INV05-GAP03 historical serial number/code immutable after fully reversed opening, audit/receipt/journal/outbox atomic, descriptive edit allowed");

        var reversedPrint = await Call(s => s.InventoryOpening.Print(cid, reversal.ResourceId, writer, default));
        Ensure(reversedPrint.ReversesOpeningId == r.ResourceId &&
            reversedPrint.State == nameof(InventoryOpeningState.Posted) &&
            reversedPrint.TotalDocumentValue == 15m &&
            reversedPrint.SignedEffectValue == -15m &&
            reversedPrint.Lines.Single().SignedEffectValue == -15m,
            "INV06 SQL reversal print did not show negative historical effect");
        Console.WriteLine("  SQL PASS INV06-OP04 read-only opening and reversal print, exact amounts, linked journal and authorization");
        var stockLedger = await Call(s => s.Report.InventoryLedger(cid, item.ResourceId, writer, Fixture.Date, Fixture.Date, branch, wh.ResourceId, null, 1, 1, default));
        Ensure(stockLedger.Rows.TotalCount == 2 && stockLedger.ClosingQuantity == 0 && stockLedger.ClosingValue == 0 && stockLedger.InValue == 15 && stockLedger.OutValue == 15 && stockLedger.Rows.Items.Single().RunningValue == 0, "SQL item ledger page balance differs");
        Ensure(stockLedger.Rows.Items.Single().DocumentUnits.Single().Quantity == -3, "SQL source document units missing");
        var hiddenZero = await Call(s => s.Report.InventoryBalances(cid, writer, Fixture.Date, Fixture.Date, branch, wh.ResourceId, group.ResourceId, item.ResourceId, false, 0, 1, default));
        Ensure(hiddenZero.Rows.TotalCount == 0, "SQL zero balance filter failed");
        var unknownIdentity = await Call(s => s.Report.InventoryLedger(cid, item.ResourceId, writer, Fixture.Date, Fixture.Date, branch, wh.ResourceId, "UNKNOWN", 0, 1, default));
        Ensure(unknownIdentity.Rows.TotalCount == 0, "SQL empty identity filter leaked movements");
        Console.WriteLine("  SQL PASS RPT08/09 filtered balances, control tie-out, reversal paging, historical units and zero/identity filters");
        var reconciled = await Call(s => s.PeriodClose.PreviewPeriodClose(cid, periodId, reviewer, 0, 10, default));
        Ensure(reconciled.Checks.Single(x => x.Code == "inventory_openings_reconciled").Passed, "Reversed opening fails reconciliation");
        Console.WriteLine("  SQL PASS INV06 reversal draft/post replay, unique source, atomic Outbox rollback, runtime posting, negative movement and linked historical journal reconciliation");
        Guid numberYear;await using(var db=Context())numberYear=(await db.Set<FiscalPeriod>().SingleAsync(x=>x.CompanyId==cid && x.Id==periodId)).FiscalYearId;
        var numberSeries=await Call(s=>s.Numbering.Create(cid,owner,Fixture.Key(),new(NumberingDocumentKind.InventoryOpening,numberYear,777,"CFGIO-"),default));
        var numbered=await Call(s=>s.InventoryOpening.Create(cid,writer,Fixture.Key(),request with {CountReference="SYS10-IO"},default));
        var numberUsage=await Call(s=>s.Numbering.Usage(cid,numberSeries.ResourceId,owner,0,20,default));
        Ensure(numbered.Number=="CFGIO-000777" && numberUsage.Items.Single().DocumentId==numbered.ResourceId,"Configured inventory number lost SQL source FK or identity");
        Console.WriteLine("  SQL PASS SYS10 configured inventory opening allocation and composite source FK");



    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
