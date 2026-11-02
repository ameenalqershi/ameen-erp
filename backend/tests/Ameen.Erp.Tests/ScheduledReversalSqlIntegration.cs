using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class ScheduledReversalSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "026_scheduled_journal_reversals.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Journals] ADD CONSTRAINT [CK_Test_ReversalCollision] CHECK ([Revision] > 0)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script.Replace("ADD CONSTRAINT [CK_Journal_AutomaticReversal]", "ADD CONSTRAINT [CK_Test_ReversalCollision]"));
            throw new Exception("Conflicting scheduled reversal migration succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=26").AnyAsync(),
            "Failed reversal migration saved version");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Journals') AND name='AutomaticReversal'").SingleAsync() == 0,
            "Failed reversal migration retained columns");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Journals] DROP CONSTRAINT [CK_Test_ReversalCollision]");
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=26").SingleAsync() == 1 &&
            await db.Set<JournalEntry>().AllAsync(x => !x.AutomaticReversal && x.AutomaticReversalDate == null),
            "Upgrade changed legacy scheduling defaults or was not idempotent");
        try
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE [Journals] SET [AutomaticReversal]=1 WHERE [AutomaticReversal]=0");
            throw new Exception("SQL accepted enabled reversal without a date");
        }
        catch (SqlException ex) when (ex.Number == 547) { }
        Console.WriteLine("  SQL PASS GL03 schema 025→026 is atomic/idempotent and preserves non-scheduled legacy journals");
    }

    public static async Task Run(string connection, Guid cid, Actor clerk, Actor approver, CreateJournalRequest example)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var request = example with { AutomaticReversal = true, ReversalDate = example.AccountingDate.AddDays(1) };
        var original = await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(), request, default));
        original = await Call(s => s.Journal.TransitionJournal(cid, original.ResourceId, clerk, Fixture.Key(), new(original.Version), "submit", default));
        original = await Call(s => s.Journal.TransitionJournal(cid, original.ResourceId, approver, Fixture.Key(), new(original.Version), "approve", default));
        original = await Call(s => s.Journal.TransitionJournal(cid, original.ResourceId, clerk, Fixture.Key(), new(original.Version), "post", default));
        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s =>
            s.Journal.RunScheduledReversals(cid, clerk, request.ReversalDate!.Value, 100, default))));
        Ensure(runs.All(x => x.GeneratedCount == 1 && x.FailedCount == 0) &&
            runs.Select(x => x.Rows.Single().ReversalJournalId).Distinct().Count() == 1,
            "Concurrent scheduled runs did not replay one command");
        var reversalId = runs[0].Rows.Single().ReversalJournalId!.Value;
        await using (var db = Context())
        {
            var reversal = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == cid && x.Id == reversalId);
            var source = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == cid && x.Id == original.ResourceId);
            Ensure(reversal.State == JournalState.Draft && reversal.Source == JournalSource.Reversal &&
                reversal.ReversesJournalId == original.ResourceId && source.PendingReversalJournalId == reversalId &&
                reversal.Lines.Sum(x => x.FunctionalCredit) == request.Lines.Sum(x => x.Debit),
                "SQL scheduled inverse is not a linked historical draft");
            Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name='IX_Journals_CompanyId_AutomaticReversalDate_State_BranchId' AND filter_definition IS NOT NULL").SingleAsync() == 1,
                "Filtered due index missing");
        }
        Ensure((await Call(s => s.Journal.RunScheduledReversals(cid, clerk, request.ReversalDate.Value, 100, default))).SelectedCount == 0,
            "SQL scheduled run duplicated a reserved reversal");
        var reversalVersion = (await Call(s => s.Journal.GetJournal(cid, reversalId, clerk, default))).Version;
        await Call(s => s.Journal.TransitionJournal(cid, reversalId, clerk, Fixture.Key(), new(reversalVersion, "إلغاء"), "cancel", default));
        Ensure((await Call(s => s.Journal.RunScheduledReversals(cid, clerk, request.ReversalDate.Value, 100, default))).SelectedCount == 0,
            "Cancelled SQL scheduled reversal regenerated");
        Console.WriteLine("  SQL PASS GL03-GP05 concurrent due runs, filtered index, draft reservation, replay and cancellation suppression");
    }

    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
