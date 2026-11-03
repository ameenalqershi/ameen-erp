using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class AccountSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk, CreateJournalRequest journalRequest)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var created = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("ACCT-001", "حساب اختبار", AccountType.Asset, null, true, false, false, null, NormalBalance.Credit, "Contra asset", null), default));
        var account = await Call(s => s.Account.GetAccount(cid, created.ResourceId, owner, default));
        var request = AccountTests.Edit(account) with { Code = "ACCT-002", EnglishName = "Concurrency cash", Notes = "ملاحظات محفوظة" };
        var key = Fixture.Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Call(s => s.Account.EditAccount(cid, account.Id, owner, key, request, default))));
        Ensure(results.Distinct().Count() == 1, "Account edit replay differs");
        account = await Call(s => s.Account.GetAccount(cid, account.Id, owner, default));
        Ensure(account.Code == "ACCT-002" && account.EnglishName == "Concurrency cash" && account.Revision == 2 && account.NormalBalance == NormalBalance.Credit && account.Notes == "ملاحظات محفوظة", "Account SQL fields differ");
        var page = await Call(s => s.Account.ListAccounts(cid, owner, "Concurrency", 0, 10, default));
        Ensure(page.Items.Single().Id == account.Id, "English search SQL translation differs");
        var audit = await Call(s => s.Account.ListAccountAudit(cid, account.Id, owner, 0, 10, default));
        Ensure(audit.Items.Count(x => x.Action == "account.edited") == 1, "Repeated edit added audits");
        var competingRequest = AccountTests.Edit(account);
        async Task<bool> Compete(string name)
        {
            try { await Call(s => s.Account.EditAccount(cid, account.Id, owner, Fixture.Key(), competingRequest with { Name = name }, default)); return true; }
            catch (BusinessException e) when (e.Code == "version_conflict") { return false; }
        }
        Ensure((await Task.WhenAll(Compete("تعديل أول"), Compete("تعديل آخر"))).Count(x => x) == 1, "Stale writers both committed");
        Console.WriteLine("  SQL PASS GL01 bilingual cards, English search, concurrent replay and competing version edits");

        account = await Call(s => s.Account.GetAccount(cid, account.Id, owner, default));
        var failureRequest = AccountTests.Edit(account) with { Notes = "يجب ألا يتسرب" }; var failureKey = Fixture.Key();
        await using (var db = Context())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_AccountAudit] CHECK ([Action] <> N'account.edited')");
        try
        {
            try { await Call(s => s.Account.EditAccount(cid, account.Id, owner, failureKey, failureRequest, default)); throw new Exception("Expected account audit failure"); }
            catch (BusinessException e) when (e.Code == "database_constraint") { }
            await using var db = Context();
            var unchanged = await db.Set<Account>().SingleAsync(x => x.CompanyId == cid && x.Id == account.Id);
            Ensure(unchanged.Version == account.Version && unchanged.Notes == account.Notes && !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == failureKey), "Failed edit leaked account or receipt");
        }
        finally
        {
            await using var db = Context(); await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_AccountAudit]");
        }
        var retry = await Call(s => s.Account.EditAccount(cid, account.Id, owner, failureKey, failureRequest, default));
        await using (var db = Context())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Accounts] SET [NormalBalance]=99 WHERE [CompanyId]={cid} AND [Id]={account.Id}");
                throw new Exception("Invalid nature bypassed database check");
            }
            catch (SqlException ex) when (ex.Number == 547) { }
        }
        Console.WriteLine("  SQL PASS GL01 failed audit rolls back account/receipt; retry succeeds; SQL rejects invalid nature");

        await using (var restricted = new SqlConnection(connection))
        {
            await restricted.OpenAsync();
            await using var command = restricted.CreateCommand(); command.CommandText = "EXECUTE AS USER = 'ameen_runtime_test'"; await command.ExecuteNonQueryAsync();
            try
            {
                await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(restricted).Options);
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                account = await service.Account.GetAccount(cid, account.Id, owner, default);
                Ensure(account.Version == retry.Version, "Retry did not persist");
                await service.Account.EditAccount(cid, account.Id, owner, Fixture.Key(), AccountTests.Edit(account) with { EnglishName = "Runtime edit" }, default);
            }
            finally { command.CommandText = "REVERT"; await command.ExecuteNonQueryAsync(); }
        }
        Console.WriteLine("  SQL PASS GL01 card edit succeeds under the runtime database role");

        var raceCard = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("ACCT-RACE", "حساب السباق", AccountType.Asset, null, true, false, false, null, NormalBalance.Debit), default));
        var raceAccount = await Call(s => s.Account.GetAccount(cid, raceCard.ResourceId, owner, default));
        var raceJournal = journalRequest with { Lines = journalRequest.Lines.Select((x, i) => i == 0 ? x with { AccountId = raceAccount.Id } : x).ToArray() };
        async Task<bool> Reconfigure()
        {
            try { await Call(s => s.Account.EditAccount(cid, raceAccount.Id, owner, Fixture.Key(), AccountTests.Edit(raceAccount) with { IsControl = true }, default)); return true; }
            catch (BusinessException e) when (e.Code == "account_structure_in_use") { return false; }
        }
        async Task<bool> Draft()
        {
            try { await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(), raceJournal, default)); return true; }
            catch (BusinessException e) when (e.Code == "control_account_protected") { return false; }
        }
        Ensure((await Task.WhenAll(Reconfigure(), Draft())).Count(x => x) == 1, "Journal/configuration race violated account usage lock");
        var used = await Call(s => s.Account.GetAccountUsage(cid, journalRequest.Lines[0].AccountId, owner, 0, 1, default));
        Ensure(used.HasUsageInSubtree && used.JournalCount > 0 && used.RecurringTemplateCount > 0 && used.References.Items.Count == 1, "SQL usage references differ");
        Console.WriteLine("  SQL PASS GL01 journal/configuration race serializes; usage includes journals and recurring templates");
        // GL01-02: this is the accepted CURRENT usage scope, not the not-yet-defined
        // migration workflow of GL01-GP01 or unreleased subsidiary-ledger sources.
        var usageAccountId = journalRequest.Lines[0].AccountId;
        async Task<(int Accounts, int Journals, int Templates, int Audits, int Receipts, int Outbox)> UsageState()
        {
            await using var db = Context();
            return (
                await db.Set<Account>().CountAsync(x => x.CompanyId == cid),
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid),
                await db.Set<RecurringJournalTemplate>().CountAsync(x => x.CompanyId == cid),
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid),
                await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid),
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == cid));
        }
        var usageBefore = await UsageState();
        var completeUsage = await Call(s => s.Account.GetAccountUsage(cid, usageAccountId, owner, 0, 100, default));
        Ensure(completeUsage.AccountId == usageAccountId &&
            completeUsage.JournalCount > 0 && completeUsage.RecurringTemplateCount > 0 &&
            completeUsage.HasUsageInSubtree && completeUsage.References.TotalCount >=
                completeUsage.JournalCount + completeUsage.RecurringTemplateCount &&
            completeUsage.References.Items.Any(x => x.Kind == "Journal") &&
            completeUsage.References.Items.Any(x => x.Kind == "RecurringTemplate"),
            "GL01-02 usage omitted journal/template references or invented scope");
        var usageFirst = await Call(s => s.Account.GetAccountUsage(cid, usageAccountId, owner, 0, 1, default));
        var usageNext = await Call(s => s.Account.GetAccountUsage(cid, usageAccountId, owner, 1, 1, default));
        Ensure(usageFirst.References.TotalCount == completeUsage.References.TotalCount &&
            usageNext.References.TotalCount == completeUsage.References.TotalCount &&
            usageFirst.References.Items.Count == 1 && usageNext.References.Items.Count == 1 &&
            usageFirst.References.Items[0].ResourceId != usageNext.References.Items[0].ResourceId &&
            completeUsage.References.Items[0].ResourceId == usageFirst.References.Items[0].ResourceId &&
            completeUsage.References.Items[1].ResourceId == usageNext.References.Items[0].ResourceId,
            "GL01-02 SQL references paging is inconsistent or duplicates sources");
        try
        {
            await Call(s => s.Account.GetAccountUsage(cid, usageAccountId,
                new Actor("gl01-usage-foreign-tenant"), 0, 10, default));
            throw new Exception("GL01-02 unrelated tenant reader obtained account usage");
        }
        catch (BusinessException e) when (e.Code == "permission_denied") { }
        Ensure(usageBefore == await UsageState(),
            "GL01-02 read-only SQL usage/unauthorized calls changed accounts, journals, templates, audit, receipts or outbox");
        Console.WriteLine("  SQL PASS GL01-02 current journal/template usage, stable paging, tenant denial and zero read side effects");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
