using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class JournalProcessingSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk, Actor approver, CreateJournalRequest request)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        async Task<CommandReceipt> Draft() => await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(), request, default));
        async Task<CommandReceipt> Approve(CommandReceipt r)
        {
            r = await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, clerk, Fixture.Key(), new(r.Version), "submit", default));
            return await Call(s => s.Journal.TransitionJournal(cid, r.ResourceId, approver, Fixture.Key(), new(r.Version), "approve", default));
        }
        var openingKey = Fixture.Key(); var openingRequest = GeneralOpeningTests.Request(request);
        var openingReplays = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.Journal.CreateGeneralOpening(cid, clerk, openingKey, openingRequest, default))));
        Ensure(openingReplays.Distinct().Count() == 1, "General opening concurrent replay duplicated source");
        var opening = openingReplays[0];
        opening = await Call(s => s.Journal.EditGeneralOpening(cid, opening.ResourceId, clerk, Fixture.Key(), new(opening.Version, "تصحيح SQL", "TB-SQL", request.Lines), default));
        opening = await Approve(opening);
        opening = await Call(s => s.Journal.TransitionJournal(cid, opening.ResourceId, approver, Fixture.Key(), new(opening.Version), "post", default));
        var reverseKey = Fixture.Key(); var reverseRequest = new ReverseJournalRequest(opening.Version, request.AccountingDate, "تصحيح افتتاح");
        var reversals = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.Journal.ReverseJournal(cid, opening.ResourceId, clerk, reverseKey, reverseRequest, default))));
        Ensure(reversals.Distinct().Count() == 1, "Opening reversal concurrent replay differs");
        var inverse = reversals[0];
        var trackedOpening = await Call(s => s.Journal.GetGeneralOpening(cid, opening.ResourceId, clerk, default));
        Ensure(trackedOpening.OpeningRevision == 6, "Reservation did not advance opening revision once");
        var cancellationKey = Fixture.Key(); var cancellation = new TransitionRequest(inverse.Version, "إلغاء العكس");
        await Call(s => s.Journal.TransitionJournal(cid, inverse.ResourceId, clerk, cancellationKey, cancellation, "cancel", default));
        await Call(s => s.Journal.TransitionJournal(cid, inverse.ResourceId, clerk, cancellationKey, cancellation, "cancel", default));
        trackedOpening = await Call(s => s.Journal.GetGeneralOpening(cid, opening.ResourceId, clerk, default));
        Ensure(trackedOpening.OpeningRevision == 7 && trackedOpening.PendingReversalJournalId == null, "Cancellation metadata or replay differs");
        inverse = await Call(s => s.Journal.ReverseJournal(cid, opening.ResourceId, clerk, Fixture.Key(), new(trackedOpening.Version, request.AccountingDate, "عكس بديل"), default));
        inverse = await Approve(inverse);
        var postOpeningKey = Fixture.Key(); var postOpening = new TransitionRequest(inverse.Version);
        var rejectOpeningAudit = "ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_OpeningTracking] CHECK ([ResourceId] <> '" + opening.ResourceId.ToString("D") + "' OR [Action] <> 'general-opening.reversal-posted')";
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync(rejectOpeningAudit);
        try
        {
            await Call(s => s.Journal.TransitionJournal(cid, inverse.ResourceId, approver, postOpeningKey, postOpening, "post", default));
            throw new Exception("Expected opening tracking audit failure");
        }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        finally
        {
            await using var db = Context();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_OpeningTracking]");
        }
        await using (var db = Context())
        {
            var original = await db.Set<JournalEntry>().SingleAsync(x => x.Id == opening.ResourceId);
            Ensure(original.OpeningRevision == 8 && original.State == JournalState.Posted && original.ReversalJournalId == null && original.PendingReversalJournalId == inverse.ResourceId, "Failed audit left original reversal metadata");
            Ensure((await db.Set<JournalEntry>().SingleAsync(x => x.Id == inverse.ResourceId)).State == JournalState.Approved, "Failed audit posted inverse");
            Ensure(!await db.Set<OutboxEvent>().AnyAsync(x => x.ResourceId == inverse.ResourceId) && !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == postOpeningKey), "Failed audit retained effect or consumed key");
        }
        var postedOpening = await Call(s => s.Journal.TransitionJournal(cid, inverse.ResourceId, approver, postOpeningKey, postOpening, "post", default));
        Ensure(postedOpening == await Call(s => s.Journal.TransitionJournal(cid, inverse.ResourceId, approver, postOpeningKey, postOpening, "post", default)), "Opening post replay differs");
        var originalOpening = await Call(s => s.Journal.GetGeneralOpening(cid, opening.ResourceId, clerk, default));
        Ensure(originalOpening.Source == JournalSource.GeneralOpening && originalOpening.State == JournalState.Reversed && originalOpening.PreviousTrialBalanceReference == "TB-SQL" && originalOpening.OpeningRevision == 9, "SQL opening metadata/reversal lost");
        await using (var db = Context())
        {
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.ResourceId == opening.ResourceId && x.Action.StartsWith("general-opening.reversal-")) == 4, "Opening reversal audit missing or duplicated");
            var protectedOpening = await db.Set<JournalEntry>().SingleAsync(x => x.Id == opening.ResourceId);
            protectedOpening.AdvanceOpening(new FixedClock().UtcNow.AddMinutes(1));
            try { await db.SaveChangesAsync(); throw new Exception("Unrelated posted metadata edit was accepted"); }
            catch (BusinessException e) when (e.Code == "posted_journal_immutable") { }
        }
        Console.WriteLine("  SQL PASS GL02 concurrent replay, draft replacement, approval posting and immutable historical reversal provenance");
        var first = await Approve(await Draft()); var rejected = await Draft(); var last = await Approve(await Draft());
        var firstJournal = await Call(s => s.Journal.GetJournal(cid, first.ResourceId, clerk, default));
        var lastJournal = await Call(s => s.Journal.GetJournal(cid, last.ResourceId, clerk, default));
        var textual = await Call(s => s.Journal.ProcessingQueue(cid, approver, firstJournal.FiscalPeriodId,
            firstJournal.BranchId, null, JournalSource.Manual, null, null, "post", 0, 10, default,
            firstJournal.Number + ".." + lastJournal.Number));
        Ensure(textual.TotalCount == 2 && textual.Items.Count == 2 &&
            textual.Items.All(x => x.CanExecute), "GL06 textual queue scope differs on SQL Server");
        Console.WriteLine("  SQL PASS GL06-GP03 fiscal-year textual range and scoped queue selection");
        var items = new[] { new JournalProcessingItem(first.ResourceId, first.Version, Fixture.Key()), new JournalProcessingItem(rejected.ResourceId, rejected.Version, Fixture.Key()), new JournalProcessingItem(last.ResourceId, last.Version, Fixture.Key()) };
        var command = new ProcessJournalsRequest("post", items);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.Journal.ProcessJournals(cid, approver, command, default))));
        Ensure(results.All(x => x.SucceededCount == 2 && x.FailedCount == 1 && !x.AllSucceeded && x.Rows[1].RejectionRecorded), "Mixed concurrent batch results differ");
        Ensure(results.All(x => x.Rows.SequenceEqual(results[0].Rows)), "Concurrent batch replay differs");
        await using (var db = Context())
        {
            var ids = items.Select(x => x.JournalId).ToArray();
            Ensure(await db.Set<OutboxEvent>().CountAsync(x => ids.Contains(x.ResourceId)) == 2, "Batch produced duplicate effects");
            Ensure(await db.Set<AuditEvent>().CountAsync(x => ids.Contains(x.ResourceId) && x.Action.StartsWith("journal.processing-")) == 3, "Batch history duplicate/missing");
        }
        var fail = await Approve(await Draft()); var succeed = await Approve(await Draft());
        var retryItems = new[] { new JournalProcessingItem(fail.ResourceId, fail.Version, Fixture.Key()), new JournalProcessingItem(succeed.ResourceId, succeed.Version, Fixture.Key()) };
        // DDL cannot parameterize a CHECK literal; the only inserted value is a generated, typed Guid.
        var failureConstraint = "ALTER TABLE [OutboxEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_ProcessingOutbox] CHECK ([ResourceId] <> '" + fail.ResourceId.ToString("D") + "')";
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync(failureConstraint);
        var durableKey = Fixture.Key();
        var durableRequest = new ProcessJournalsRequest("post", retryItems, durableKey);
        var partial = await Call(s => s.Journal.ProcessJournals(cid, approver, durableRequest, default));
        Ensure(partial.Rows[0].ErrorCode == "database_constraint" && !partial.Rows[0].RejectionRecorded && partial.Rows[1].Succeeded, "Failed document prevented independent success");
        var historical = await Call(s => s.Journal.DurableBatch(cid, approver, durableKey, default));
        Ensure(historical.Rows.Length == 2 && historical.Rows[0].ErrorCode == "database_constraint" &&
            !historical.Rows[0].RejectionRecorded && historical.Rows[1].Succeeded &&
            historical.Revision == 3 && historical.FinishedAt.HasValue,
            "GL06 batch did not persist technical failure outside rolled-back journal transaction");
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] DROP CONSTRAINT [CK_Test_ProcessingOutbox]");
            Ensure(!await db.Set<AuditEvent>().AnyAsync(x => x.ResourceId == fail.ResourceId && x.Action.StartsWith("journal.processing-")), "Failed transaction left history");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == retryItems[0].IdempotencyKey), "Failed transaction consumed key");
        }
        var retried = await Call(s => s.Journal.ProcessJournals(cid, approver, durableRequest, default));
        Ensure(retried.AllSucceeded && retried.Rows[1].Receipt == partial.Rows[1].Receipt, "Retry duplicated successful peer");
        var recovered = await Call(s => s.Journal.DurableBatch(cid, approver, durableKey, default));
        Ensure(recovered.AllSucceeded && recovered.Revision == 4 && recovered.Rows.All(x => x.Succeeded) &&
            recovered.FinishedAt >= historical.FinishedAt,
            "GL06 batch did not recover failed item or increment numeric action revision");
        await using (var db = Context())
        {
            var batch = await db.Set<JournalProcessingBatch>().SingleAsync(x => x.CompanyId == cid &&
                x.BatchKey == durableKey);
            Ensure(batch.Revision == 4 && batch.Rows().Length == 2 &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.ResourceId == batch.Id &&
                    x.Action == "journal.processing.batch-item") == 3,
                "GL06 technical-failure attempt/audit history was not immutable across retry");
        }
        // AT03: replay the finished durable batch from a fresh SQL scope. A replay must
        // not increment its revision, append another decision audit or emit another event.
        var recoveredReplay = await Call(s => s.Journal.ProcessJournals(cid, approver, durableRequest, default));
        Ensure(recoveredReplay.Rows.SequenceEqual(retried.Rows) && recoveredReplay.AllSucceeded,
            "AT03 durable SQL replay changed the original posting receipts");
        await using (var db = Context())
        {
            var batch = await db.Set<JournalProcessingBatch>().SingleAsync(x => x.CompanyId == cid && x.BatchKey == durableKey);
            var ids = retryItems.Select(x => x.JournalId).ToArray();
            Ensure(batch.Revision == 4 && batch.Rows().Length == retryItems.Length,
                "AT03 replay advanced an already completed durable batch");
            Ensure(await db.Set<OutboxEvent>().CountAsync(x => ids.Contains(x.ResourceId)) == 2,
                "AT03 replay duplicated journal outbox effects");
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.ResourceId == batch.Id &&
                x.Action == "journal.processing.batch-item") == 3,
                "AT03 replay appended a duplicate batch decision audit");
        }
        try
        {
            await Call(s => s.Journal.ProcessJournals(cid, approver,
                durableRequest with { Items = retryItems.Reverse().ToArray() }, default));
            throw new Exception("AT03 reused batch key with different request was accepted");
        }
        catch (BusinessException e) when (e.Code == "idempotency_conflict") { }
        Console.WriteLine("  SQL PASS AT03 durable completed-batch replay, stable revision/outbox/audit and changed-payload rejection");
        Console.WriteLine("  SQL PASS GL06-GP02 durable technical errors, independent retry, action revision and audit history");
        // Simulate stale tracked membership/period reads in a reused scoped service, then revoke externally.
        await using (var db = Context())
        {
            var services = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
            var member = await db.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == approver.Subject);
            var originalPermissions = member.GrantedPermissions.ToArray();
            await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(), new(approver.Subject,
                originalPermissions.Where(x => x != Permissions.Post).ToArray(), member.AllBranches, member.BranchIds.ToArray(), member.Version), default));
            try
            {
                var stale = await services.Journal.ProcessJournals(cid, approver, command, default);
                Ensure(stale.Rows.All(x => !x.Succeeded && x.ErrorCode == "permission_denied" && x.Receipt == null), "Tracked membership bypassed fresh replay authorization");
            }
            finally
            {
                await using var fresh = Context(); var current = await fresh.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == approver.Subject);
                await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(), new(approver.Subject, originalPermissions, current.AllBranches, current.BranchIds.ToArray(), current.Version), default));
            }
        }
        Console.WriteLine("  SQL PASS GL05/06 mixed concurrent batch replay, durable rejection, per-document rollback/retry, history and refreshed authorization");
        // GL06-T02 extra concurrency acceptance: the same durable key must not create
        // multiple batch headers, decision records or financial outbox events.
        var concurrentApproved = await Approve(await Draft());
        var concurrentBatchKey = Fixture.Key();
        var concurrentItem = new JournalProcessingItem(concurrentApproved.ResourceId,
            concurrentApproved.Version, Fixture.Key());
        var concurrentRequest = new ProcessJournalsRequest("post",
            new[] { concurrentItem }, concurrentBatchKey);
        var concurrentResults = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Call(s => s.Journal.ProcessJournals(cid, approver, concurrentRequest, default))));
        Ensure(concurrentResults.All(x => x.AllSucceeded && x.Rows.Length == 1 &&
            x.Rows[0].Receipt == concurrentResults[0].Rows[0].Receipt),
            "GL06 concurrent durable batch replays diverged");
        await using (var db = Context())
        {
            var batches = await db.Set<JournalProcessingBatch>().Where(x => x.CompanyId == cid &&
                x.BatchKey == concurrentBatchKey).ToArrayAsync();
            Ensure(batches.Length == 1 && batches[0].Revision == 2 &&
                batches[0].Rows().Length == 1,
                "GL06 concurrent durable replay duplicated or advanced the batch header");
            Ensure(await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid &&
                x.ResourceId == concurrentApproved.ResourceId) == 1,
                "GL06 concurrent durable replay duplicated financial outbox events");
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid &&
                x.ResourceId == batches[0].Id && x.Action == "journal.processing.batch-item") == 1,
                "GL06 concurrent durable replay duplicated batch decision history");
        }
        Console.WriteLine("  SQL PASS GL06 concurrent durable batch key preserves one header, one decision and one outbox");
        // GL02-GP01: verify the real persisted SQL snapshot, posted reversal and dated FX constraint.
        await Call(s => s.Currency.CreateCurrency(cid, owner, Fixture.Key(), new("CHF", 2, "فرنك"), default));
        var datedRate = await Call(s => s.ExchangeRate.CreateExchangeRate(cid, owner, Fixture.Key(),
            new("CHF", request.AccountingDate, 3.125m, "سعر الافتتاح"), default));
        var foreignLines = request.Lines.Select((x, i) => x with {
            CurrencyCode = "CHF", ExchangeRateId = datedRate.ResourceId,
            Debit = i == 0 ? 20m : 0m, Credit = i == 1 ? 20m : 0m }).ToArray();
        var foreignRequest = GeneralOpeningTests.Request(request) with {
            CurrencyCode = "CHF", ExchangeRate = 3.125m,
            PreviousTrialBalanceReference = "TB-CHF-SQL", Lines = foreignLines };
        var foreignKey = Fixture.Key();
        var foreign = await Call(s => s.Journal.CreateGeneralOpening(cid, clerk, foreignKey, foreignRequest, default));
        Ensure(foreign == await Call(s => s.Journal.CreateGeneralOpening(cid, clerk, foreignKey, foreignRequest, default)),
            "Foreign opening idempotency differs in SQL");
        var foreignApproved = await Approve(foreign);
        foreign = await Call(s => s.Journal.TransitionJournal(cid, foreignApproved.ResourceId, approver, Fixture.Key(),
            new(foreignApproved.Version), "post", default));
        await using (var db = Context())
        {
            var snapshot = await db.Set<JournalEntry>().SingleAsync(x => x.Id == foreign.ResourceId);
            Ensure(snapshot.OpeningCurrencyCode == "CHF" && snapshot.OpeningExchangeRate == 3.125m &&
                snapshot.Lines.All(x => x.ExchangeRateId == datedRate.ResourceId && x.ExchangeRate == 3.125m) &&
                snapshot.Lines.Sum(x => x.FunctionalDebit) == 62.5m, "SQL foreign opening snapshot differs");
        }
        var foreignInverse = await Call(s => s.Journal.ReverseJournal(cid, foreign.ResourceId, clerk, Fixture.Key(),
            new(foreign.Version, request.AccountingDate, "تصحيح افتتاح أجنبي"), default));
        foreignInverse = await Approve(foreignInverse);
        await Call(s => s.Journal.TransitionJournal(cid, foreignInverse.ResourceId, approver, Fixture.Key(),
            new(foreignInverse.Version), "post", default));
        await using (var db = Context())
        {
            var originalFx = await db.Set<JournalEntry>().SingleAsync(x => x.Id == foreign.ResourceId);
            var inverseFx = await db.Set<JournalEntry>().SingleAsync(x => x.Id == foreignInverse.ResourceId);
            Ensure(originalFx.State == JournalState.Reversed &&
                originalFx.Lines.Sum(x => x.FunctionalDebit - x.FunctionalCredit) +
                inverseFx.Lines.Sum(x => x.FunctionalDebit - x.FunctionalCredit) == 0 &&
                inverseFx.Lines.All(x => x.ExchangeRateId == datedRate.ResourceId && x.ExchangeRate == 3.125m),
                "SQL FX reversal recomputed historical rates or amounts");
        }
        Console.WriteLine("  SQL PASS GL02-GP01 dated FX opening 018, replay, immutable snapshot and historical reversal");

    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
