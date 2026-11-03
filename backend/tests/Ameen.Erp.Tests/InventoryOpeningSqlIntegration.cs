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
