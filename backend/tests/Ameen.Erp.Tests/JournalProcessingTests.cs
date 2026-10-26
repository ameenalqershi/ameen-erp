using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class JournalProcessingTests
{
    private static JournalProcessingItem Item(CommandReceipt r, string? reason = null) => new(r.ResourceId, r.Version, Fixture.Key(), reason);
    private static Task<JournalProcessingResult> Run(Fixture f, string action, JournalProcessingItem[] items, Actor? actor = null) =>
        f.Service.Journal.ProcessJournals(f.CompanyId, actor ?? f.Approver, new(action, items), default);
    private static async Task<CommandReceipt> Approved(Fixture f) => await f.Action(await f.Action(await f.Draft(), "submit"), "approve", f.Approver);
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-A01: journal search finds number, description and external reference without leaking branch", async f =>
        {
            var first = await f.Draft(f.Request() with { Description = "MANUAL-SEARCH-ALPHA", ExternalReference = "EXT-MANUAL-ALPHA" });
            var second = await f.Draft(f.Request(branch: f.OtherBranchId) with { Description = "MANUAL-SEARCH-BETA", ExternalReference = "EXT-MANUAL-BETA" });
            var result = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.Manual, "EXT-MANUAL-ALPHA");
            Ensure(result.TotalCount == 1 && result.Items.Single().Id == first.ResourceId);
            result = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.Manual, "MANUAL-SEARCH-BETA");
            Ensure(result.TotalCount == 1 && result.Items.Single().Id == second.ResourceId);
            result = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, f.BranchId, null, 0, 1, default,
                JournalSource.Manual, "MANUAL-SEARCH");
            Ensure(result.TotalCount == 1 && result.Items.Single().Id == first.ResourceId);
            var confined = await f.Service.Journal.ListJournals(f.CompanyId, f.Limited, null, null, 0, 10, default,
                JournalSource.Manual, "EXT-MANUAL-BETA");
            Ensure(confined.TotalCount == 0);
            var oldNumber = (await f.Journal(first.ResourceId)).Number;
            result = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default,
                JournalSource.Manual, oldNumber);
            Ensure(result.TotalCount == 1 && result.Items.Single().Id == first.ResourceId);
            await Error("search_too_long", () => f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null,
                0, 10, default, JournalSource.Manual, new string('x', 101)));
        });
        test("GL06-GP03: textual journal number range matches the selected fiscal year, branch and state without writes", async f =>
        {
            var first = await Approved(f);
            var rejected = await f.Draft();
            var last = await Approved(f);
            var a = await f.Journal(first.ResourceId);
            var b = await f.Journal(last.ResourceId);
            var auditCount = f.Store.Count<AuditEvent>();
            var range = a.Number + ".." + b.Number;
            var preview = await f.Service.Journal.ProcessingQueue(f.CompanyId, f.Approver, a.FiscalPeriodId,
                f.BranchId, null, JournalSource.Manual, null, null, "post", 0, 1, default, range);
            Ensure(preview.TotalCount == 2 && preview.Items.Count == 1 && preview.Items[0].Journal.Id == first.ResourceId);
            var single = await f.Service.Journal.ProcessingQueue(f.CompanyId, f.Approver, a.FiscalPeriodId,
                f.BranchId, null, JournalSource.Manual, null, null, "post", 0, 10, default, b.Number);
            Ensure(single.TotalCount == 1 && single.Items.Single().Journal.Id == last.ResourceId);
            var decimalRange = await f.Service.Journal.ProcessingQueue(f.CompanyId, f.Approver, a.FiscalPeriodId,
                f.BranchId, null, JournalSource.Manual, null, null, "post", 0, 10, default,
                a.Sequence + ".." + b.Sequence);
            Ensure(decimalRange.TotalCount == 2);
            await Error("invalid_journal_number_range", () => f.Service.Journal.ProcessingQueue(f.CompanyId,
                f.Approver, a.FiscalPeriodId, null, null, null, null, null, "post", 0, 10, default,
                "JV-1900-000001"));
            await Error("invalid_journal_number_range", () => f.Service.Journal.ProcessingQueue(f.CompanyId,
                f.Approver, a.FiscalPeriodId, null, null, null, null, null, "post", 0, 10, default,
                b.Number + ".." + a.Number));
            await Error("invalid_journal_number_range", () => f.Service.Journal.ProcessingQueue(f.CompanyId,
                f.Approver, a.FiscalPeriodId, null, null, null, null, null, "post", 0, 10, default,
                "1..2..3"));
            await Error("ambiguous_number_range", () => f.Service.Journal.ProcessingQueue(f.CompanyId,
                f.Approver, a.FiscalPeriodId, null, null, null, 1, 3, "post", 0, 10, default, range));
            Ensure((await f.Journal(rejected.ResourceId)).State == JournalState.Draft &&
                f.Store.Count<AuditEvent>() == auditCount);
        });
        test("GL05/06: mixed batch commits valid document and records invalid document without all-success claim", async f =>
        {
            var approved = await Approved(f); var draft = await f.Draft(); var items = new[] { Item(approved), Item(draft) };
            var result = await Run(f, "post", items);
            Ensure(!result.AllSucceeded && result.SucceededCount == 1 && result.FailedCount == 1);
            Ensure(result.Rows[0].Succeeded && result.Rows[1].RejectionRecorded && result.Rows[1].ErrorCode == "journal_state_conflict");
            Ensure((await f.Journal(approved.ResourceId)).State == JournalState.Posted && (await f.Journal(draft.ResourceId)).State == JournalState.Draft);
            var replay = await Run(f, "post", items); Ensure(replay.Rows.SequenceEqual(result.Rows));
            Ensure(f.Store.Count<OutboxEvent>() == 1 && f.Store.All<AuditEvent>().Count(x => x.Action.StartsWith("journal.processing-")) == 2);
        });
        test("GL05: batch approval preserves separate approver and return requires a reason", async f =>
        {
            var submitted = await f.Action(await f.Draft(), "submit");
            var denied = await Run(f, "approve", new[] { Item(submitted) }, f.Clerk);
            Ensure(denied.Rows.Single().ErrorCode == "self_approval_forbidden" && denied.Rows.Single().RejectionRecorded);
            var returned = await Run(f, "return", new[] { Item(submitted) }); Ensure(returned.Rows.Single().ErrorCode == "invalid_text");
            var corrected = await Run(f, "return", new[] { Item(submitted, "تصحيح البيان") }); Ensure(corrected.AllSucceeded);
            Ensure((await f.Journal(submitted.ResourceId)).State == JournalState.Draft);
        });
        test("GL05/06: queue filters period branch state source and number range with no writes", async f =>
        {
            var submitted = await f.Action(await f.Draft(), "submit"); await f.Draft();
            var j = await f.Journal(submitted.ResourceId); var audits = f.Store.Count<AuditEvent>();
            var page = await f.Service.Journal.ProcessingQueue(f.CompanyId, f.Approver, j.FiscalPeriodId, f.BranchId, null, JournalSource.Manual,
                j.Sequence, j.Sequence, "approve", 0, 1, default);
            Ensure(page.TotalCount == 1 && page.Items.Single().CanExecute && page.Items.Single().Journal.Id == j.Id);
            var noRows = await f.Service.Journal.ProcessingQueue(f.CompanyId, f.Approver, j.FiscalPeriodId, f.OtherBranchId, null, null, null, null, "approve", 0, 1, default);
            Ensure(noRows.TotalCount == 0 && f.Store.Count<AuditEvent>() == audits);
        });
        test("GL06: preview does not reserve state and execution revalidates versions", async f =>
        {
            var r = await Approved(f); var p = await f.Service.Journal.ProcessingPreview(f.CompanyId, r.ResourceId, f.Approver, "post", null, default);
            Ensure(p.CanExecute && p.Journal.Version == r.Version);
            await f.Action(r, "post"); var result = await Run(f, "post", new[] { Item(r) });
            Ensure(result.Rows.Single().ErrorCode == "version_conflict" && f.Store.Count<OutboxEvent>() == 1);
        });
        test("GL05/06: rejected decision replays after state changes and new key permits a new decision", async f =>
        {
            var draft = await f.Draft(); var item = Item(draft); var rejected = await Run(f, "post", new[] { item });
            var approved = await f.Action(await f.Action(draft, "submit"), "approve", f.Approver);
            Ensure((await Run(f, "post", new[] { item })).Rows.SequenceEqual(rejected.Rows));
            var changed = await Run(f, "post", new[] { item with { ExpectedVersion = approved.Version } });
            Ensure(changed.Rows.Single().ErrorCode == "idempotency_conflict");
            Ensure((await Run(f, "post", new[] { Item(approved) })).AllSucceeded);
        });
        test("GL05/06: duplicate and malformed selection rejects entire request before effects", async f =>
        {
            var r = await Approved(f); var item = Item(r);
            await Error("duplicate_processing_item", () => Run(f, "post", new[] { item, item }));
            await Error("invalid_processing_count", () => Run(f, "post", Array.Empty<JournalProcessingItem>()));
            await Error("invalid_processing_action", () => Run(f, "delete", new[] { item }));
            Ensure((await f.Journal(r.ResourceId)).State == JournalState.Approved && f.Store.Count<OutboxEvent>() == 0);
        });
        test("GL05/06: foreign reference and denied branches do not expose journal receipts", async f =>
        {
            var own = await f.Draft(); var other = await f.Draft(f.Request(branch: f.OtherBranchId));
            var result = await Run(f, "post", new[] { Item(own), Item(other), new JournalProcessingItem(Guid.NewGuid(), Guid.NewGuid(), Fixture.Key()) }, f.Limited);
            Ensure(result.Rows[1].ErrorCode == "branch_access_denied" && result.Rows[1].Receipt is null && !result.Rows[1].RejectionRecorded);
            Ensure(result.Rows[2].ErrorCode == "reference_not_found" && result.Rows[2].Receipt is null);
        });
        test("GL06: failed persistence rolls back posting and processing history then exact retry succeeds", async f =>
        {
            var r = await Approved(f); var item = Item(r); var count = f.Store.Count<AuditEvent>(); f.Store.FailBeforeCommit = true;
            try { await Run(f, "post", new[] { item }); throw new Exception("Expected IO failure"); } catch (IOException) { }
            Ensure(f.Store.Count<AuditEvent>() == count && f.Store.Count<OutboxEvent>() == 0 && (await f.Journal(r.ResourceId)).State == JournalState.Approved);
            Ensure((await Run(f, "post", new[] { item })).AllSucceeded);
        });
        test("GL05/06: rejection history is immutable audited scoped and paginated", async f =>
        {
            var r = await f.Draft(); await Run(f, "post", new[] { Item(r) });
            var history = await f.Service.Journal.ProcessingHistory(f.CompanyId, r.ResourceId, f.Owner, 0, 1, default);
            Ensure(history.TotalCount == 1 && history.Items.Single().Action == "journal.processing-rejected");
            await Error("permission_denied", () => f.Service.Journal.ProcessingHistory(f.CompanyId, r.ResourceId, f.Clerk, 0, 1, default));
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Journal processing assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException ex) when (ex.Code == code) { return; } throw new Exception("Expected " + code); }
}
