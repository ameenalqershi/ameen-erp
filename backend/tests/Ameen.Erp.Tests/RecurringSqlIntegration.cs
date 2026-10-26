using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class RecurringSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor clerk, Actor approver, CreateJournalRequest example)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var lines = example.Lines.Select(x => new RecurringLineRequest(x.AccountId, x.CostCenterId, x.Description, x.CurrencyCode, x.Debit, x.Credit)).ToArray();
        var request = new CreateRecurringJournalRequest(example.BranchId, "SQL recurring", RecurringFrequency.Monthly,
            RecurringGenerationMode.Manual, new(2026, 1, 31), new(2026, 12, 31), lines);
        var template = await Call(s => s.RecurringJournal.CreateRecurringJournal(cid, clerk, Fixture.Key(), request, default));
        Dictionary<int, Guid> periods;
        await using (var db = Context())
            periods = await db.Set<FiscalPeriod>().Where(x => x.CompanyId == cid).ToDictionaryAsync(x => x.StartsOn.Month, x => x.Id);
        Task<CommandReceipt> Generate(CommandReceipt t, int month, string key) => Call(s => s.RecurringJournal.GenerateRecurringJournal(cid, t.ResourceId,
            clerk, key, new(t.Version, periods[month]), default));
        var sameKey = Fixture.Key();
        var simultaneous = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Generate(template, 10, sameKey)));
        Ensure(simultaneous.Distinct().Count() == 1, "Same-key SQL recurrence differs");
        await using (var db = Context())
            Ensure(await db.Set<RecurringJournalGeneration>().CountAsync(x => x.CompanyId == cid && x.TemplateId == template.ResourceId) == 1, "Multiple occurrences saved");

        async Task<CommandReceipt?> Compete()
        {
            try { return await Generate(template, 2, Fixture.Key()); }
            catch (BusinessException ex) when (ex.Code == "recurring_already_generated") { return null; }
        }
        var distinctKeys = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Compete()));
        Ensure(distinctKeys.Count(x => x is not null) == 1, "Distinct-key requests generated multiple drafts");
        Console.WriteLine("  SQL PASS recurring generation is unique per template/period across identical and distinct concurrent keys");

        var changed = lines.Select(x => x with { Debit = x.Debit * 2, Credit = x.Credit * 2 }).ToArray();
        var edited = await Call(s => s.RecurringJournal.EditRecurringJournal(cid, template.ResourceId, clerk, Fixture.Key(),
            new(template.Version, request.Name, request.Frequency, request.GenerationMode, request.StartsOn, request.EndsOn, changed), default));
        await using (var revisionDb = Context())
        {
            var card = await revisionDb.Set<RecurringJournalTemplate>().SingleAsync(x =>
                x.CompanyId == cid && x.Id == template.ResourceId);
            Ensure(card.Revision == 2 && card.LastModifiedAt >= card.CreatedAt &&
                card.Version == edited.Version, "GL04 numeric revision not persisted on SQL edit");
        }
        var december = await Generate(edited, 12, Fixture.Key());
        await using (var db = Context())
        {
            var old = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == cid && x.Id == simultaneous[0].ResourceId);
            var fresh = await db.Set<JournalEntry>().SingleAsync(x => x.CompanyId == cid && x.Id == december.ResourceId);
            Ensure(old.SourceTemplateVersion == template.Version && old.Lines.Sum(x => x.Debit) == 100, "Template edit rewrote old draft");
            Ensure(fresh.SourceTemplateVersion == edited.Version && fresh.Lines.Sum(x => x.Debit) == 200, "Template edit did not affect new generation");
        }

        long sequence; int journals, audits;
        await using (var db = Context())
        {
            sequence = await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).Select(x => x.LastNumber).SingleAsync();
            journals = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid);
            audits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            var ddl = "ALTER TABLE [RecurringJournalGenerations] WITH NOCHECK ADD CONSTRAINT [CK_Test_RecurringFailure] CHECK ([TemplateId] <> '" + template.ResourceId.ToString("D") + "')";
            await db.Database.ExecuteSqlRawAsync(ddl);
        }
        var failureKey = Fixture.Key();
        await Expect("database_constraint", () => Generate(edited, 9, failureKey));
        await using (var db = Context())
        {
            Ensure(await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) == journals, "Generation failure leaked a draft");
            Ensure(await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == audits, "Generation failure leaked an audit");
            Ensure(await db.Set<NumberSequence>().Where(x => x.CompanyId == cid).Select(x => x.LastNumber).SingleAsync() == sequence, "Generation failure consumed a number");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == failureKey), "Failed generation saved a receipt");
            Ensure(!await db.Set<RecurringJournalGeneration>().AnyAsync(x => x.CompanyId == cid && x.TemplateId == template.ResourceId && x.FiscalPeriodId == periods[9]), "Failed generation saved its occurrence");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [RecurringJournalGenerations] DROP CONSTRAINT [CK_Test_RecurringFailure]");
        }
        await Generate(edited, 9, failureKey);
        Console.WriteLine("  SQL PASS recurring failure rolls back journal, occurrence, numbering, audit and receipt; identical retry succeeds");

        await using (var db = Context())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO [RecurringJournalGenerations]
([CompanyId],[Id],[Version],[TemplateId],[TemplateVersion],[FiscalPeriodId],[JournalId],[DueDate],[GeneratedBy],[GeneratedAt])
SELECT [CompanyId],NEWID(),NEWID(),[TemplateId],[TemplateVersion],[FiscalPeriodId],[JournalId],[DueDate],[GeneratedBy],[GeneratedAt]
FROM [RecurringJournalGenerations] WHERE [CompanyId]={cid} AND [TemplateId]={template.ResourceId} AND [FiscalPeriodId]={periods[10]}");
                throw new Exception("Duplicate occurrence bypassed the SQL unique index");
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { }
            var foreign = await db.Set<Account>().Where(x => x.CompanyId != cid).Select(x => x.Id).FirstAsync();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [RecurringJournalLines] SET [AccountId]={foreign} WHERE [CompanyId]={cid} AND [TemplateId]={template.ResourceId}");
                throw new Exception("Foreign account bypassed recurring line FK");
            }
            catch (SqlException ex) when (ex.Number == 547) { }
        }

        await using (var sql = new SqlConnection(connection))
        {
            await sql.OpenAsync();
            await Execute(sql, "EXECUTE AS USER = 'ameen_runtime_test'");
            try
            {
                await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(sql).Options);
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                await service.RecurringJournal.EditRecurringJournal(cid, template.ResourceId, clerk, Fixture.Key(),
                    new(edited.Version, "Runtime template", request.Frequency, request.GenerationMode, request.StartsOn, request.EndsOn, lines), default);
                try
                {
                    await Execute(sql, "UPDATE [RecurringJournalGenerations] SET [GeneratedBy]=[GeneratedBy] WHERE 1=0");
                    throw new Exception("Runtime role can modify recurring history");
                }
                catch (SqlException ex) when (ex.Number == 229) { }
            }
            finally { await Execute(sql, "REVERT"); }
        }
        var first = simultaneous[0];
        first = await Call(s => s.Journal.TransitionJournal(cid, first.ResourceId, clerk, Fixture.Key(), new(first.Version), "submit", default));
        first = await Call(s => s.Journal.TransitionJournal(cid, first.ResourceId, approver, Fixture.Key(), new(first.Version), "approve", default));
        first = await Call(s => s.Journal.TransitionJournal(cid, first.ResourceId, clerk, Fixture.Key(), new(first.Version), "post", default));
        Ensure(first.Status == "Posted", "Recurring draft could not complete independent workflow");
        var sourcePage = await Call(s => s.Journal.ListJournals(cid, clerk, null, null, 0, 10, default, JournalSource.Recurring));
        Ensure(sourcePage.TotalCount == 4 && sourcePage.Items.All(x => x.RecurringTemplateId == template.ResourceId), "SQL source filter differs");
        Console.WriteLine("  SQL PASS recurring source FK/uniqueness, immutable history, runtime-role template edit and independent posting");

        // GL04-GP01: schedule mode persisted by schema 019, no financial posting, safe replays.
        var scheduledRequest = request with { Name = "SQL scheduled drafts", GenerationMode = RecurringGenerationMode.Scheduled };
        var scheduled = await Call(s => s.RecurringJournal.CreateRecurringJournal(cid, clerk, Fixture.Key(), scheduledRequest, default));
        var due = await Call(s => s.RecurringJournal.RunScheduledDue(cid, clerk, new DateOnly(2026, 9, 30), 100, default));
        Ensure(due.GeneratedCount == 9 && due.FailedCount == 0 &&
            due.Rows.All(x => x.TemplateId == scheduled.ResourceId && x.JournalId.HasValue),
            "SQL scheduler missed a due period or generated an unrelated manual template");
        var again = await Call(s => s.RecurringJournal.RunScheduledDue(cid, clerk, new DateOnly(2026, 9, 30), 100, default));
        Ensure(again.SelectedCount == 0, "SQL scheduled run repeated an occurrence");
        await using (var db = Context())
        {
            var occurrences = await db.Set<RecurringJournalGeneration>().Where(x => x.CompanyId == cid &&
                x.TemplateId == scheduled.ResourceId).ToListAsync();
            Ensure(occurrences.Count == 9 && occurrences.Select(x => x.FiscalPeriodId).Distinct().Count() == 9,
                "SQL scheduler duplicated a template/period");
            var ids = occurrences.Select(x => x.JournalId).ToArray();
            var drafts = await db.Set<JournalEntry>().Where(x => x.CompanyId == cid && ids.Contains(x.Id)).ToListAsync();
            Ensure(drafts.Count == 9 && drafts.All(x => x.State == JournalState.Draft &&
                x.PostedAt == null && x.RecurringTemplateId == scheduled.ResourceId) &&
                !await db.Set<OutboxEvent>().AnyAsync(x => x.CompanyId == cid && ids.Contains(x.ResourceId)),
                "SQL scheduled generation automatically posted or lost provenance");
        }
        Console.WriteLine("  SQL PASS GL04-GP01 scheduled mode, nine due periods, replay uniqueness and draft-only accounting");

    }
    private static async Task Execute(SqlConnection connection, string text)
    {
        await using var command = connection.CreateCommand(); command.CommandText = text; await command.ExecuteNonQueryAsync();
    }
    private static async Task Expect(string code, Func<Task> work)
    {
        try { await work(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected business error " + code);
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
