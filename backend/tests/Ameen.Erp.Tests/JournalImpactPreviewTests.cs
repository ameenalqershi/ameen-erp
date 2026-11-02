using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class JournalImpactPreviewTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-A05: balanced draft impact exposes exact functional debits and credits without writes", async f =>
        {
            var r = await f.Draft(f.Request(135m, 135m));
            var journal = await f.Journal(r.ResourceId);
            var audits = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var sequences = f.Store.Count<NumberSequence>();
            var before = f.Store.Count<OutboxEvent>();
            var result = await f.Service.Journal.ImpactPreview(f.CompanyId, r.ResourceId, f.Clerk, default);
            Ensure(result.JournalId == r.ResourceId && result.Version == journal.Version &&
                result.State == JournalState.Draft && result.Source == JournalSource.Manual &&
                result.CanSubmit && result.IsBalanced && result.ValidationErrorCode is null &&
                result.FunctionalDebit == 135m && result.FunctionalCredit == 135m &&
                result.Difference == 0m && result.Lines.Length == 2);
            Ensure(result.Lines.Sum(x => x.FunctionalNet) == 0m &&
                result.Lines.Sum(x => x.FunctionalDebit) == 135m &&
                result.Lines.Sum(x => x.FunctionalCredit) == 135m);
            Ensure(f.Store.Count<AuditEvent>() == audits && f.Store.Count<IdempotencyRecord>() == receipts &&
                f.Store.Count<NumberSequence>() == sequences && f.Store.Count<OutboxEvent>() == before &&
                (await f.Journal(r.ResourceId)).Version == r.Version);
        });
        test("GL03-A05: unbalanced draft remains unchanged and preview explains why it cannot submit", async f =>
        {
            var r = await f.Draft(f.Request(135m, 130m));
            var beforeAudit = f.Store.Count<AuditEvent>();
            var preview = await f.Service.Journal.ImpactPreview(f.CompanyId, r.ResourceId, f.Clerk, default);
            Ensure(!preview.CanSubmit && !preview.IsBalanced && preview.ValidationErrorCode == "journal_unbalanced" &&
                preview.Difference == 5m && preview.FunctionalDebit == 135m && preview.FunctionalCredit == 130m);
            Ensure((await f.Journal(r.ResourceId)).State == JournalState.Draft &&
                f.Store.Count<AuditEvent>() == beforeAudit && f.Store.Count<OutboxEvent>() == 0);
        });
        test("GL03-A05: user without matching branch or membership cannot inspect account lines", async f =>
        {
            var r = await f.Draft(f.Request(branch:f.OtherBranchId));
            await Error("branch_access_denied", () =>
                f.Service.Journal.ImpactPreview(f.CompanyId, r.ResourceId, f.Limited, default));
            await Error("permission_denied", () =>
                f.Service.Journal.ImpactPreview(f.CompanyId, r.ResourceId, new Actor("foreign-user"), default));
        });
    }
    private static void Ensure(bool condition)
    {
        if (!condition) throw new Exception("Journal impact preview assertion failed");
    }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected business error " + code);
    }
}
