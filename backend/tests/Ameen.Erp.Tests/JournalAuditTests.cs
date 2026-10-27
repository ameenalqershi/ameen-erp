using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class JournalAuditTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-FG08: numeric revision covers editing, workflow and source reversal links once per command", async f =>
        {
            var r = await f.Draft();
            var created = await f.Journal(r.ResourceId); var createdAt = created.CreatedAt;
            Ensure(created.Revision == 1 && created.LastModifiedAt == createdAt && created.CreatedBy == f.Clerk.Subject);
            var body = new EditJournalRequest(r.Version, "تعديل مدقق", f.Request().Lines); var key = Fixture.Key();
            r = await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, key, body, default);
            Ensure(r == await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, key, body, default));
            Ensure((await f.Journal(r.ResourceId)).Revision == 2);
            r = await f.Action(r, "submit"); r = await f.Action(r, "return", f.Approver, reason: "مراجعة");
            r = await f.Action(r, "submit"); r = await f.Action(r, "approve", f.Approver); r = await f.Action(r, "post");
            Ensure((await f.Journal(r.ResourceId)).Revision == 7);
            var reverseBody = new ReverseJournalRequest(r.Version, Fixture.Date, "عكس مدقق"); key = Fixture.Key();
            var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, r.ResourceId, f.Clerk, key, reverseBody, default);
            Ensure(reverse == await f.Service.Journal.ReverseJournal(f.CompanyId, r.ResourceId, f.Clerk, key, reverseBody, default));
            Ensure((await f.Journal(r.ResourceId)).Revision == 8 && (await f.Journal(reverse.ResourceId)).Revision == 1);
            await f.Action(reverse, "cancel", reason: "تحرير الحجز");
            var original = await f.Journal(r.ResourceId); Ensure(original.Revision == 9);
            reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(),
                new(original.Version, Fixture.Date, "عكس نهائي"), default);
            await f.Post(reverse);
            original = await f.Journal(r.ResourceId);
            Ensure(original.Revision == 11 && original.State == JournalState.Reversed && original.CreatedAt == createdAt &&
                original.CreatedBy == f.Clerk.Subject && original.LastModifiedAt >= createdAt && original.Lines.Sum(x => x.Debit) == 100);
            var page = await f.Service.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 0, 10, default);
            Ensure(page.Items.Single(x => x.Id == r.ResourceId).Revision == original.Revision);
        });
        test("GL03-FG08: failed edit and failed reversal posting restore revision timestamp audit and receipt", async f =>
        {
            var r = await f.Draft(); var original = await f.Journal(r.ResourceId);
            var version = original.Version; var modified = original.LastModifiedAt; var auditCount = f.Store.Count<AuditEvent>();
            var request = new EditJournalRequest(r.Version, "فشل مدقق", f.Request(250,250).Lines); var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, key, request, default); throw new Exception("Expected failure"); }
            catch (IOException) { }
            original = await f.Journal(r.ResourceId);
            Ensure(original.Version == version && original.Revision == 1 && original.LastModifiedAt == modified &&
                f.Store.Count<AuditEvent>() == auditCount && !f.Store.All<IdempotencyRecord>().Any(x => x.Key == key));
            r = await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, key, request, default);
            try { await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), request, default); throw new Exception("Expected stale version"); }
            catch (BusinessException ex) when (ex.Code == "version_conflict") { }
            Ensure((await f.Journal(r.ResourceId)).Revision == 2);
            r = await f.Post(r); original = await f.Journal(r.ResourceId);
            var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "فشل ترحيل"), default);
            reverse = await f.Action(reverse, "submit"); reverse = await f.Action(reverse, "approve", f.Approver);
            var sourceRevision = (await f.Journal(r.ResourceId)).Revision;
            f.Store.FailBeforeCommit = true;
            try { await f.Action(reverse, "post"); throw new Exception("Expected posting failure"); }
            catch (IOException) { }
            Ensure((await f.Journal(r.ResourceId)).Revision == sourceRevision && (await f.Journal(reverse.ResourceId)).Revision == 3);
        });
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("Journal audit assertion failed"); }
}
