using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// GL03-F10/F11/F12: server-managed state and Unicode / zero-preserving
// journal header validation. GL03-F09 header FX remains a separate policy gap.
public static class JournalHeaderFieldsTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-FG03: Unicode description, leading-zero reference and system state are persisted and searchable", async f =>
        {
            const string description = "قيد يومية – اختبار عربي آمن";
            const string reference = "0000472-INV";
            var request = f.Request() with { Description = description, ExternalReference = reference };
            var created = await f.Draft(request);
            var entry = await f.Journal(created.ResourceId);
            Ensure(entry.Description == description && entry.ExternalReference == reference &&
                entry.State == JournalState.Draft && entry.Source == JournalSource.Manual);
            var lookup = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, f.BranchId,
                JournalState.Draft, 0, 10, default, JournalSource.Manual, reference);
            Ensure(lookup.TotalCount == 1 && lookup.Items.Single().Id == created.ResourceId &&
                lookup.Items.Single().Description == description &&
                lookup.Items.Single().State == JournalState.Draft);

            const string editedDescription = "بيان محدث دون تغيير طرف القيد";
            const string editedReference = "00000091";
            var header = new DraftJournalHeaderRequest(f.BranchId, Fixture.Date, Fixture.Date,
                editedReference);
            var editRequest = new EditJournalRequest(created.Version, editedDescription,
                request.Lines, header);
            var editKey = Fixture.Key();
            // MemoryStore returns the tracked entity: snapshot its revision before editing.
            var initialRevision = entry.Revision;
            var auditCount = f.Store.Count<AuditEvent>();
            var changed = await f.Service.Journal.EditJournal(f.CompanyId, created.ResourceId,
                f.Clerk, editKey, editRequest, default);
            var replay = await f.Service.Journal.EditJournal(f.CompanyId, created.ResourceId,
                f.Clerk, editKey, editRequest, default);
            var updated = await f.Journal(created.ResourceId);
            Ensure(changed == replay && updated.Description == editedDescription &&
                updated.ExternalReference == editedReference && updated.State == JournalState.Draft &&
                updated.Revision == initialRevision + 1 &&
                f.Store.Count<AuditEvent>() == auditCount + 1);
            var oldSearch = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk,
                f.BranchId, JournalState.Draft, 0, 10, default, JournalSource.Manual, reference);
            var newSearch = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk,
                f.BranchId, JournalState.Draft, 0, 10, default, JournalSource.Manual, editedReference);
            Ensure(oldSearch.TotalCount == 0 && newSearch.Items.Single().Id == created.ResourceId);
            var submitted = await f.Action(changed, "submit");
            Ensure((await f.Journal(submitted.ResourceId)).State == JournalState.Submitted,
                "The state must change only through the authorized workflow");
        });

        test("GL03-FG03: invalid text and references fail atomically without reserving numbers", async f =>
        {
            var drafts = f.Store.Count<JournalEntry>();
            var audits = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var allocated = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            async Task Reject(CreateJournalRequest req)
            {
                var key = Fixture.Key();
                try
                {
                    await f.Draft(req, key);
                    throw new Exception("Invalid journal header was accepted");
                }
                catch (BusinessException ex) when (ex.Code == "invalid_text") { }
                Ensure(!f.Store.All<IdempotencyRecord>().Any(x => x.Key == key));
            }
            await Reject(f.Request() with { Description = "   " });
            await Reject(f.Request() with { Description = "محتوى\u0001تحكم" });
            await Reject(f.Request() with { ExternalReference = new string('R', 101) });
            await Reject(f.Request() with { ExternalReference = "0001\n002" });
            Ensure(f.Store.Count<JournalEntry>() == drafts &&
                f.Store.Count<AuditEvent>() == audits &&
                f.Store.Count<IdempotencyRecord>() == receipts &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == allocated,
                "Rejected GL03 header changed journals, audit, receipts or numbering");
        });
    }

    private static void Ensure(bool condition, string? message = null)
    {
        if (!condition) throw new Exception(message ?? "GL03 header field acceptance failed");
    }
}
