using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// GL03-F10/F11/F12 acceptance against an isolated disposable SQL database.
// The header exchange-rate policy (GL03-F09) is intentionally not inferred.
public static class JournalHeaderFieldsSqlIntegration
{
    public static async Task Run(string connection, Guid companyId, Actor clerk, CreateJournalRequest example)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Context();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }

        const string originalDescription = "اختبار بيان اليومية باللغة العربية";
        const string originalReference = "00000571-GL";
        var initial = example with { Description = originalDescription, ExternalReference = originalReference };
        var saved = await Call(s => s.Journal.CreateJournal(companyId, clerk, Fixture.Key(), initial, default));
        await using (var db = Context())
        {
            var persisted = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == companyId
                && x.Id == saved.ResourceId);
            Ensure(persisted.Description == originalDescription &&
                persisted.ExternalReference == originalReference &&
                persisted.State == JournalState.Draft && persisted.Revision == 1,
                "GL03 header Unicode, leading-zero reference or system state not persisted");
        }

        var byReference = await Call(s => s.Journal.ListJournals(companyId, clerk,
            example.BranchId, JournalState.Draft, 0, 10, default, JournalSource.Manual, originalReference));
        Ensure(byReference.Items.Any(x => x.Id == saved.ResourceId),
            "GL03 reference not searchable under branch/state/source scope");

        int initialJournals, initialAudits, initialReceipts;
        long initialNumber;
        await using (var db = Context())
        {
            initialJournals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == companyId);
            initialAudits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId);
            initialReceipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == companyId);
            initialNumber = await db.Set<NumberSequence>().Where(x => x.CompanyId == companyId)
                .SumAsync(x => x.LastNumber);
        }

        var invalidKey = Fixture.Key();
        try
        {
            await Call(s => s.Journal.CreateJournal(companyId, clerk, invalidKey,
                initial with { ExternalReference = new string('Z', 101) }, default));
            throw new Exception("Overlong external reference was accepted by SQL service");
        }
        catch (BusinessException ex) when (ex.Code == "invalid_text") { }

        await using (var db = Context())
        {
            Ensure(await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == companyId) == initialJournals &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId) == initialAudits &&
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == companyId) == initialReceipts &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == companyId && x.Key == invalidKey) &&
                await db.Set<NumberSequence>().Where(x => x.CompanyId == companyId)
                    .SumAsync(x => x.LastNumber) == initialNumber,
                "Rejected GL03 header affected journal/audit/idempotency/numbering in SQL");
        }

        const string amendedReference = "000000097";
        const string amendedDescription = "تعديل البيان والتحقق من الإصدار";
        var edit = new EditJournalRequest(saved.Version, amendedDescription, example.Lines,
            new DraftJournalHeaderRequest(example.BranchId, example.DocumentDate,
                example.AccountingDate, amendedReference));
        var editKey = Fixture.Key();
        var first = await Call(s => s.Journal.EditJournal(companyId, saved.ResourceId, clerk,
            editKey, edit, default));
        var replay = await Call(s => s.Journal.EditJournal(companyId, saved.ResourceId, clerk,
            editKey, edit, default));
        Ensure(first == replay, "GL03 changed edit replay of optional reference");
        await using (var db = Context())
        {
            var persisted = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == companyId
                && x.Id == saved.ResourceId);
            Ensure(persisted.Description == amendedDescription &&
                persisted.ExternalReference == amendedReference &&
                persisted.Revision == 2 && persisted.State == JournalState.Draft &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId &&
                    x.ResourceId == saved.ResourceId && x.Action == "journal.edited") == 1,
                "GL03 edited header reference/description/state/revision differs in SQL");
        }
        var found = await Call(s => s.Journal.ListJournals(companyId, clerk,
            example.BranchId, JournalState.Draft, 0, 10, default, JournalSource.Manual, amendedReference));
        Ensure(found.Items.Any(x => x.Id == saved.ResourceId),
            "GL03 updated reference not available through tenant-scoped SQL search");

        Console.WriteLine("  SQL PASS GL03-FG03 Unicode description, zero-preserved reference, server state, atomic rejection and edit replay");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
