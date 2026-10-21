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
        async Task<T> Call<T>(Func<ErpService, Task<T>> work)
        {
            await using var db = Context();
            return await work(new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var created = await Call(s => s.CreateAccount(cid, owner, Fixture.Key(),
            new("ACCT-001", "حساب اختبار", AccountType.Asset, null, true, false, false, null, NormalBalance.Credit, "Contra asset", null), default));
        var account = await Call(s => s.GetAccount(cid, created.ResourceId, owner, default));
        var request = AccountTests.Edit(account) with { Code = "ACCT-002", EnglishName = "Concurrency cash", Notes = "ملاحظات محفوظة" };
        var key = Fixture.Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Call(s => s.EditAccount(cid, account.Id, owner, key, request, default))));
        Ensure(results.Distinct().Count() == 1, "Account edit replay differs");
        account = await Call(s => s.GetAccount(cid, account.Id, owner, default));
        Ensure(account.Code == "ACCT-002" && account.EnglishName == "Concurrency cash" && account.Revision == 2 && account.NormalBalance == NormalBalance.Credit && account.Notes == "ملاحظات محفوظة", "Account SQL fields differ");
        var page = await Call(s => s.ListAccounts(cid, owner, "Concurrency", 0, 10, default));
        Ensure(page.Items.Single().Id == account.Id, "English search SQL translation differs");
        var audit = await Call(s => s.ListAccountAudit(cid, account.Id, owner, 0, 10, default));
        Ensure(audit.Items.Count(x => x.Action == "account.edited") == 1, "Repeated edit added audits");
        var competingRequest = AccountTests.Edit(account);
        async Task<bool> Compete(string name)
        {
            try { await Call(s => s.EditAccount(cid, account.Id, owner, Fixture.Key(), competingRequest with { Name = name }, default)); return true; }
            catch (BusinessException e) when (e.Code == "version_conflict") { return false; }
        }
        Ensure((await Task.WhenAll(Compete("تعديل أول"), Compete("تعديل آخر"))).Count(x => x) == 1, "Stale writers both committed");
        Console.WriteLine("  SQL PASS GL01 bilingual cards, English search, concurrent replay and competing version edits");

        account = await Call(s => s.GetAccount(cid, account.Id, owner, default));
        var failureRequest = AccountTests.Edit(account) with { Notes = "يجب ألا يتسرب" }; var failureKey = Fixture.Key();
        await using (var db = Context())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_AccountAudit] CHECK ([Action] <> N'account.edited')");
        try
        {
            try { await Call(s => s.EditAccount(cid, account.Id, owner, failureKey, failureRequest, default)); throw new Exception("Expected account audit failure"); }
            catch (BusinessException e) when (e.Code == "database_constraint") { }
            await using var db = Context();
            var unchanged = await db.Set<Account>().SingleAsync(x => x.CompanyId == cid && x.Id == account.Id);
            Ensure(unchanged.Version == account.Version && unchanged.Notes == account.Notes && !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == failureKey), "Failed edit leaked account or receipt");
        }
        finally
        {
            await using var db = Context(); await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_AccountAudit]");
        }
        var retry = await Call(s => s.EditAccount(cid, account.Id, owner, failureKey, failureRequest, default));
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
                var service = new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                account = await service.GetAccount(cid, account.Id, owner, default);
                Ensure(account.Version == retry.Version, "Retry did not persist");
                await service.EditAccount(cid, account.Id, owner, Fixture.Key(), AccountTests.Edit(account) with { EnglishName = "Runtime edit" }, default);
            }
            finally { command.CommandText = "REVERT"; await command.ExecuteNonQueryAsync(); }
        }
        Console.WriteLine("  SQL PASS GL01 card edit succeeds under the runtime database role");

        var raceCard = await Call(s => s.CreateAccount(cid, owner, Fixture.Key(),
            new("ACCT-RACE", "حساب السباق", AccountType.Asset, null, true, false, false, null, NormalBalance.Debit), default));
        var raceAccount = await Call(s => s.GetAccount(cid, raceCard.ResourceId, owner, default));
        var raceJournal = journalRequest with { Lines = journalRequest.Lines.Select((x, i) => i == 0 ? x with { AccountId = raceAccount.Id } : x).ToArray() };
        async Task<bool> Reconfigure()
        {
            try { await Call(s => s.EditAccount(cid, raceAccount.Id, owner, Fixture.Key(), AccountTests.Edit(raceAccount) with { IsControl = true }, default)); return true; }
            catch (BusinessException e) when (e.Code == "account_structure_in_use") { return false; }
        }
        async Task<bool> Draft()
        {
            try { await Call(s => s.CreateJournal(cid, clerk, Fixture.Key(), raceJournal, default)); return true; }
            catch (BusinessException e) when (e.Code == "control_account_protected") { return false; }
        }
        Ensure((await Task.WhenAll(Reconfigure(), Draft())).Count(x => x) == 1, "Journal/configuration race violated account usage lock");
        var used = await Call(s => s.GetAccountUsage(cid, journalRequest.Lines[0].AccountId, owner, 0, 1, default));
        Ensure(used.HasUsageInSubtree && used.JournalCount > 0 && used.RecurringTemplateCount > 0 && used.References.Items.Count == 1, "SQL usage references differ");
        Console.WriteLine("  SQL PASS GL01 journal/configuration race serializes; usage includes journals and recurring templates");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
