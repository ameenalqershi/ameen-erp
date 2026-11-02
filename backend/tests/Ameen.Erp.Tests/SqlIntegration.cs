using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Explicit opt-in: creates/drops only a freshly named database on the supplied test server.
// No existing application database is migrated, seeded, or dropped by this suite.
public static class SqlIntegration
{
    public static async Task Run()
    {
        var supplied = Environment.GetEnvironmentVariable("AMEEN_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(supplied))
            throw new InvalidOperationException("--sql requires AMEEN_TEST_SQL_CONNECTION for a disposable test server.");
        var name = "AmeenErp_Test_" + Guid.NewGuid().ToString("N");
        var masterOptions = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master" };
        await using var master = new SqlConnection(masterOptions.ConnectionString);
        await master.OpenAsync();
        await Execute(master, $"CREATE DATABASE [{name}]");
        try
        {
            var options = new SqlConnectionStringBuilder(supplied) { InitialCatalog = name };
            var f = new SqlFixture(options.ConnectionString);
            await using (var db = f.Context())
            {
                var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "001_initial.sql"));
                await db.Database.ExecuteSqlRawAsync(script);
                await db.Database.ExecuteSqlRawAsync(script); // A second deployment is a no-op.
                await SchemaUpgradeIntegration.Run(db);
                await ScheduledReversalSqlIntegration.Upgrade(db);
                await JournalDocumentTypeSqlIntegration.Upgrade(db);
                await ApprovalRouteSqlIntegration.Upgrade(db);
                await SecurityRoleSqlIntegration.Upgrade(db);
            }
            await f.Initialize();
            await JournalDocumentTypeSqlIntegration.Guard(options.ConnectionString, f.CompanyId);
            await ApprovalRouteSqlIntegration.Guard(options.ConnectionString, f.CompanyId);
            await SecurityRoleSqlIntegration.Guard(options.ConnectionString, f.CompanyId);

            var key = Fixture.Key(); var request = f.Request();
            var drafts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
                f.Call(s => s.Journal.CreateJournal(f.CompanyId, f.Clerk, key, request, default))));
            Ensure(drafts.Select(x => x.ResourceId).Distinct().Count() == 1, "Concurrent replay created multiple journals");
            await using (var db = f.Context())
                Ensure(await db.Set<JournalEntry>().CountAsync() == 1, "Expected one SQL journal");
            Console.WriteLine("  SQL PASS concurrent idempotency across independent connections");

            var edited = await f.Call(s => s.Journal.EditJournal(f.CompanyId, drafts[0].ResourceId, f.Clerk, Fixture.Key(),
                new(drafts[0].Version, "تعديل محفوظ", f.Request(125).Lines), default));
            var submitted = await f.Action(edited, "submit");
            var approved = await f.Action(submitted, "approve", f.Approver);
            var postKey = Fixture.Key();
            var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Action(approved, "post", key: postKey)));
            Ensure(posts.Distinct().Count() == 1, "Posting receipts differ");
            await using (var db = f.Context())
            {
                Ensure(await db.Set<OutboxEvent>().CountAsync() == 1, "Duplicate SQL outbox");
                var entry = await db.Set<JournalEntry>().SingleAsync();
                Ensure(entry.Lines.Count == 2 && entry.Lines.Sum(x => x.FunctionalDebit) == 125, "Draft replacement did not persist");
                Ensure(entry.Revision == 5 && entry.CreatedBy == f.Clerk.Subject && entry.LastModifiedAt >= entry.CreatedAt,
                    "GL03-FG08 SQL edit/submit/approve/concurrent post did not advance revision once");
            }
            Console.WriteLine("  SQL PASS draft replacement, materialization, posting replay and one outbox");

            var second = await f.Call(s => s.Journal.CreateJournal(f.CompanyId, f.Clerk, Fixture.Key(), f.Request(), default));
            second = await f.Action(await f.Action(second, "submit"), "approve", f.Approver);
            int auditCount;
            await using (var db = f.Context())
            {
                auditCount = await db.Set<AuditEvent>().CountAsync();
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_RejectInsert] CHECK ([Type] <> N'ledger.journal-posted.v1')");
            }
            var failureKey = Fixture.Key();
            await Expect("database_constraint", () => f.Action(second, "post", key: failureKey));
            await using (var db = f.Context())
            {
                Ensure((await db.Set<JournalEntry>().SingleAsync(x => x.Id == second.ResourceId)).State == JournalState.Approved, "Failed post changed SQL state");
                Ensure(await db.Set<AuditEvent>().CountAsync() == auditCount, "Failed post leaked an audit");
                Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == failureKey), "Failed post saved a receipt");
                Ensure(await db.Set<OutboxEvent>().CountAsync() == 1, "Failed post leaked an outbox");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [OutboxEvents] DROP CONSTRAINT [CK_Test_RejectInsert]");
            }
            await f.Action(second, "post", key: failureKey);
            Console.WriteLine("  SQL PASS real constraint failure rolls back journal, audit, outbox and receipt; retry succeeds");

            var source = posts[0];
            var reverse = await f.Call(s => s.Journal.ReverseJournal(f.CompanyId, source.ResourceId, f.Clerk, Fixture.Key(),
                new(source.Version, Fixture.Date.AddDays(1), "عكس تكاملي"), default));
            reverse = await f.Action(await f.Action(reverse, "submit"), "approve", f.Approver);
            await f.Action(reverse, "post");
            var report = await f.Call(s => s.Report.TrialBalance(f.CompanyId, f.Clerk, new(2026, 1, 1), new(2026, 12, 31), null, default));
            Ensure(report.TotalClosingDebit == 100 && report.TotalClosingCredit == 100, "SQL reversal totals differ");
            var page = await f.Call(s => s.Journal.ListJournals(f.CompanyId, f.Clerk, null, null, 1, 1, default));
            Ensure(page.TotalCount == 3 && page.Items.Count == 1, "SQL pagination differs");
            Console.WriteLine("  SQL PASS linked reversal, reports, SQL translation and pagination");

            var center = await f.Call(s => s.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("FILTER", "مركز اختبار التقارير", f.BranchId), default));
            var filteredRequest = f.Request(75) with { ExternalReference = "Ref-Case" };
            filteredRequest = filteredRequest with { Lines = filteredRequest.Lines.Select(x => x with { CostCenterId = center.ResourceId }).ToArray() };
            var filtered = await f.Call(s => s.Journal.CreateJournal(f.CompanyId, f.Clerk, Fixture.Key(), filteredRequest, default));
            filtered = await f.Action(await f.Action(filtered, "submit"), "approve", f.Approver);
            await f.Action(filtered, "post");
            var centerTrial = await f.Call(s => s.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default, center.ResourceId, 1, false));
            Ensure(centerTrial.Rows.Count == 2 && centerTrial.TotalPeriodDebit == 75, "SQL center/hierarchy filter differs");
            var filteredLedger = await f.Call(s => s.Report.AccountLedger(f.CompanyId, f.CashId, f.Clerk, Fixture.Date, Fixture.Date, null, 0, 10, default, center.ResourceId, "Ref-Case"));
            Ensure(filteredLedger.Entries.TotalCount == 1 && filteredLedger.ClosingBalance == 75, "SQL reference filter differs");
            var differentCase = await f.Call(s => s.Report.AccountLedger(f.CompanyId, f.CashId, f.Clerk, Fixture.Date, Fixture.Date, null, 0, 10, default, center.ResourceId, "ref-case"));
            Ensure(differentCase.Entries.TotalCount == 0, "SQL exact reference comparison is case insensitive");
            Console.WriteLine("  SQL PASS report center, hierarchy and exact reference filters");

            var november = await f.Call(s => s.Journal.CreateJournal(f.CompanyId, f.Clerk, Fixture.Key(), f.Request(date: new(2026, 11, 1)), default));
            november = await f.Action(await f.Action(november, "submit"), "approve", f.Approver);
            FiscalPeriod period;
            await using (var db = f.Context())
                period = await db.Set<FiscalPeriod>().SingleAsync(x => x.StartsOn == new DateOnly(2026, 11, 1));
            async Task<bool> Close()
            {
                try
                {
                    await f.Call(s => s.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver, Fixture.Key(), new(period.Version, "إقفال"), true, default));
                    return true;
                }
                catch (BusinessException ex) when (ex.Code == "unposted_journals") { return false; }
            }
            var close = Close(); var posting = f.Action(november, "post");
            await Task.WhenAll(close, posting);
            if (!await close) await Close();
            await using (var db = f.Context())
            {
                Ensure((await db.Set<FiscalPeriod>().SingleAsync(x => x.Id == period.Id)).IsClosed, "Period not closed");
                Ensure((await db.Set<JournalEntry>().SingleAsync(x => x.Id == november.ResourceId)).State == JournalState.Posted, "Close/post race corrupted state");
            }
            Console.WriteLine("  SQL PASS period closure and posting serialize correctly");

            // Bypass the application on purpose to prove the composite FK rejects a foreign account.
            var other = Guid.NewGuid();
            await f.Call(s => s.Company.CreateCompany(f.Owner, Fixture.Key(), new(other, "OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default));
            var otherAccount = await f.Call(s => s.Account.CreateAccount(other, f.Owner, Fixture.Key(),
                new("1", "حساب", AccountType.Asset, null, true, false, false, null, NormalBalance.Debit), default));
            await using (var db = f.Context())
            {
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [JournalLines] SET [AccountId]={otherAccount.ResourceId} WHERE [CompanyId]={f.CompanyId} AND [AccountId]={f.CashId}");
                    throw new Exception("Cross-company SQL update unexpectedly succeeded");
                }
                catch (SqlException ex) when (ex.Number == 547) { }
            }
            Console.WriteLine("  SQL PASS database-level company isolation");
            await using (var db = f.Context())
            {
                await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "runtime-role.sql")));
                await db.Database.ExecuteSqlRawAsync("CREATE USER [ameen_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [ameen_runtime_test]");
            }
            await using (var restrictedConnection = new SqlConnection(options.ConnectionString))
            {
                await restrictedConnection.OpenAsync();
                await Execute(restrictedConnection, "EXECUTE AS USER = 'ameen_runtime_test'");
                try
                {
                    await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(restrictedConnection).Options);
                    var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                    await service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("RUNTIME", "اختبار صلاحية التشغيل", "مدينة الاختبار", "عنوان الاختبار"), default);
                    try
                    {
                        await Execute(restrictedConnection, "UPDATE [AuditEvents] SET [ActorId]=[ActorId] WHERE 1=0");
                        throw new Exception("Runtime role unexpectedly can update audit history");
                    }
                    catch (SqlException ex) when (ex.Number == 229) { }
                }
                finally { await Execute(restrictedConnection, "REVERT"); }
            }
            Console.WriteLine("  SQL PASS runtime role can save commands and cannot modify audit history");
            await JournalProcessingSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner, f.Clerk, f.Approver, f.Request());
            await DraftHeaderSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await RecurringSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await ScheduledReversalSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await AccountSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner, f.Clerk, f.Request());
            await FoundationSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner, f.Clerk, f.Request());
            await WarehouseSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await InventoryIdentitySqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await ItemSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await ItemGroupSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await UnitSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await FiscalCalendarSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await BranchSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await CompanySqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await AuditSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await HostedApiIntegration.Run(options.ConnectionString, f.CompanyId, f.Request());
            await InventoryOpeningSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.BranchId, f.Owner, f.Clerk, f.Approver);
            await NumberingSqlIntegration.Run(options.ConnectionString,f.CompanyId,f.Owner,f.Request());
            await FinancialMappingSqlIntegration.Run(options.ConnectionString,f.CompanyId,f.Owner,f.Request());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }

    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected business error " + code);
    }
    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SqlFixture
    {
        private readonly string connection;
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Actor Owner { get; } = new("sql-owner", true);
        public Actor Clerk { get; } = new("sql-clerk");
        public Actor Approver { get; } = new("sql-approver");
        public Guid BranchId { get; private set; }
        public Guid CashId { get; private set; }
        public Guid EquityId { get; private set; }
        public SqlFixture(string connection) { this.connection = connection; }
        public ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        public async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context();
            return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        public async Task Initialize()
        {
            await Call(s => s.Company.CreateCompany(Owner, Fixture.Key(), new(CompanyId, "SQLTEST", "اختبار SQL", "YE", "SAR", 2, Fixture.Profile()), default));
            BranchId = (await Call(s => s.Branch.CreateBranch(CompanyId, Owner, Fixture.Key(), new("MAIN", "الرئيسي", "مدينة الاختبار", "عنوان الاختبار"), default))).ResourceId;
            await Call(s => s.FiscalCalendar.CreateFiscalYear(CompanyId, Owner, Fixture.Key(), new("2026", new(2026, 1, 1), new(2026, 12, 31)), default));
            foreach (var actor in new[] { Clerk, Approver })
                await Call(s => s.Membership.GrantMembership(CompanyId, Owner, Fixture.Key(), new(actor.Subject, Permissions.All, true, Array.Empty<Guid>(), null), default));
            CashId = (await Call(s => s.Account.CreateAccount(CompanyId, Owner, Fixture.Key(), new("1", "نقدية", AccountType.Asset, null, true, false, false, null, NormalBalance.Debit), default))).ResourceId;
            EquityId = (await Call(s => s.Account.CreateAccount(CompanyId, Owner, Fixture.Key(), new("3", "رأس المال", AccountType.Equity, null, true, false, false, null, NormalBalance.Credit), default))).ResourceId;
        }
        public CreateJournalRequest Request(decimal amount = 100, DateOnly? date = null) => new(BranchId, date ?? Fixture.Date, date ?? Fixture.Date, "قيد SQL", null,
            new[] { new JournalLineRequest(CashId, null, "مدين", "SAR", null, amount, 0), new JournalLineRequest(EquityId, null, "دائن", "SAR", null, 0, amount) });
        public Task<CommandReceipt> Action(CommandReceipt receipt, string action, Actor? actor = null, string? key = null) =>
            Call(s => s.Journal.TransitionJournal(CompanyId, receipt.ResourceId, actor ?? Clerk, key ?? Fixture.Key(), new(receipt.Version), action, default));
    }
}
