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
        test("GL07-A05: reversal impact preview inverts original historical line amounts without mutation", async f =>
        {
            var original = await f.Post(await f.Draft(f.Request(175m, 175m)));
            var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId,
                f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "Correction"), default);
            var journal = await f.Journal(original.ResourceId); // Compare with the state after reservation.
            var reversal = await f.Journal(reverse.ResourceId);
            var audits = f.Store.Count<AuditEvent>();
            var outbox = f.Store.Count<OutboxEvent>();
            var count = f.Store.Count<JournalEntry>();
            var originalPreview = await f.Service.Journal.ImpactPreview(f.CompanyId, original.ResourceId, f.Clerk, default);
            var reversedPreview = await f.Service.Journal.ImpactPreview(f.CompanyId, reverse.ResourceId, f.Clerk, default);
            Ensure(reversedPreview.JournalId == reversal.Id && reversedPreview.Source == JournalSource.Reversal &&
                reversedPreview.State == JournalState.Draft && reversedPreview.CanSubmit &&
                reversedPreview.IsBalanced && reversedPreview.Difference == 0m &&
                reversedPreview.FunctionalDebit == originalPreview.FunctionalCredit &&
                reversedPreview.FunctionalCredit == originalPreview.FunctionalDebit);
            Ensure(reversedPreview.Lines.Length == originalPreview.Lines.Length);
            foreach (var originalLine in originalPreview.Lines)
            {
                var reversedLine = reversedPreview.Lines.Single(x => x.Position == originalLine.Position);
                Ensure(reversedLine.AccountId == originalLine.AccountId &&
                    reversedLine.CostCenterId == originalLine.CostCenterId &&
                    reversedLine.CurrencyCode == originalLine.CurrencyCode &&
                    reversedLine.ExchangeRateId == originalLine.ExchangeRateId &&
                    reversedLine.ExchangeRate == originalLine.ExchangeRate &&
                    reversedLine.FunctionalNet == -originalLine.FunctionalNet &&
                    reversedLine.Debit == originalLine.Credit &&
                    reversedLine.Credit == originalLine.Debit);
            }
            Ensure(f.Store.Count<AuditEvent>() == audits && f.Store.Count<OutboxEvent>() == outbox &&
                f.Store.Count<JournalEntry>() == count &&
                (await f.Journal(original.ResourceId)).Version == journal.Version &&
                (await f.Journal(reverse.ResourceId)).Version == reverse.Version);
        });
        test("GL07-A05: authorized reversal preview refuses another branch", async f =>
        {
            var original = await f.Post(await f.Draft(f.Request(branch: f.OtherBranchId)));
            var reverse = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId,
                f.Clerk, Fixture.Key(), new(original.Version, Fixture.Date, "Correction"), default);
            await Error("branch_access_denied", () =>
                f.Service.Journal.ImpactPreview(f.CompanyId, reverse.ResourceId, f.Limited, default));
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
