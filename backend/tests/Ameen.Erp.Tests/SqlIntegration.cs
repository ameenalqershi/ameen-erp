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
                await SecurityRoleAssignmentSqlIntegration.Upgrade(db);
                await CashBoxSqlIntegration.Upgrade(db);
                await BankAccountSqlIntegration.Upgrade(db);
                await CashCountSqlIntegration.Upgrade(db);
                await CashReceiptSqlIntegration.Upgrade(db);
                await ChequeSqlIntegration.Upgrade(db);
                await ChequeMovementSqlIntegration.Upgrade(db);
                await PaymentTermSqlIntegration.Upgrade(db);
                await CashAdvanceSqlIntegration.Upgrade(db);
                await BankReconciliationSqlIntegration.Upgrade(db);
                await GeneralOptionsSqlIntegration.Upgrade(db);
                await PrintTemplateSqlIntegration.Upgrade(db);
                await DataImportSqlIntegration.Upgrade(db);
                await BackupConfigurationSqlIntegration.Upgrade(db);
                await UserDataScopeSqlIntegration.Upgrade(db);
                await FiscalYearAuditSqlIntegration.Upgrade(db);
                await MembershipAuditSqlIntegration.Upgrade(db);
                await CustomerSqlIntegration.Upgrade(db);
                await InventoryMovementReasonSqlIntegration.Upgrade(db);
                await InventoryDocumentFoundationSqlIntegration.Upgrade(db);
            }
            await f.Initialize();
            await using (var fiscalAuditDb = f.Context())
                await FiscalYearAuditSqlIntegration.Guard(fiscalAuditDb, f.CompanyId);
            await using (var membershipAuditDb = f.Context())
                await MembershipAuditSqlIntegration.Guard(membershipAuditDb, f.CompanyId, f.Owner.Subject);
            await using (var customerDb = f.Context())
                await CustomerSqlIntegration.Guard(customerDb, f.CompanyId, f.EquityId, f.Owner.Subject);
            await using (var reasonDb = f.Context())
                await InventoryMovementReasonSqlIntegration.Guard(reasonDb, f.CompanyId, f.BranchId, f.EquityId, f.Owner.Subject);
            await using (var inventoryDocDb = f.Context())
                await InventoryDocumentFoundationSqlIntegration.Guard(inventoryDocDb, f.CompanyId, f.BranchId, f.CashId);
            await using (var paymentSchemaDb = f.Context())
                await PaymentTermSqlIntegration.Guard(paymentSchemaDb, f.CompanyId, f.BranchId, f.CashId);
            await using (var advanceSchemaDb = f.Context())
                await CashAdvanceSqlIntegration.Guard(advanceSchemaDb, f.CompanyId, f.BranchId);
            await using (var reconDb = f.Context())
                await BankReconciliationSqlIntegration.Guard(reconDb, f.CompanyId, f.BranchId);
            await using (var optionsDb = f.Context())
                await GeneralOptionsSqlIntegration.Guard(optionsDb, f.CompanyId, f.BranchId, f.CashId);
            await using (var templatesDb = f.Context())
                await PrintTemplateSqlIntegration.Guard(templatesDb, f.CompanyId, f.BranchId);
            await using (var dataImportDb = f.Context())
                await DataImportSqlIntegration.Guard(dataImportDb, f.CompanyId, f.BranchId);
            await using (var backupsDb = f.Context())
                await BackupConfigurationSqlIntegration.Guard(backupsDb, f.CompanyId, f.BranchId);
            await using (var dataScopeDb = f.Context())
                await UserDataScopeSqlIntegration.Guard(dataScopeDb, f.CompanyId, f.BranchId, f.Owner.Subject);
            await MembershipStatusSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            await MembershipUsageSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner);
            // CASH02 structural acceptance does not activate banking API/EF writes.
            await using (var bankGuardDb = f.Context())
                await BankAccountSqlIntegration.Guard(bankGuardDb, f.CompanyId, f.CashId, f.BranchId);

            // CASH01 acceptance: SQL persistence, tenant FK, atomic command replay and account guard.
            Guid cashCustodian;
            await using (var db = f.Context())
                cashCustodian = await db.Set<Membership>().Where(x => x.CompanyId == f.CompanyId &&
                    x.Subject == f.Clerk.Subject).Select(x => x.Id).SingleAsync();
            var cashRequest = new CreateCashBoxRequest("SQL-CASH", "صندوق قاعدة البيانات",
                f.CashId, f.BranchId, cashCustodian, "SAR", 100m);
            var cashKey = Fixture.Key();
            var cashReceipts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                f.Call(s => s.CashBox.CreateCashBox(f.CompanyId, f.Owner, cashKey, cashRequest, default))));
            Ensure(cashReceipts.All(x => x == cashReceipts[0]), "CASH01 concurrent SQL replay diverged");
            await using (var db = f.Context())
            {
                var card = await db.Set<CashBox>().SingleAsync(x => x.CompanyId == f.CompanyId);
                Ensure(card.Id == cashReceipts[0].ResourceId && card.CurrencyCode == "SAR" &&
                    card.BalanceLimit == 100m && card.Revision == 1 &&
                    card.CreatedBy == f.Owner.Subject && card.CreatedAt == card.LastModifiedAt,
                    "CASH01 SQL field or audit persistence mismatch");
                Ensure(await db.Set<AuditEvent>().CountAsync(x =>
                    x.CompanyId == f.CompanyId && x.Action == "cashbox.created") == 1,
                    "CASH01 replay created extra audit events");
            }
            await using (var cashGuardDb = f.Context())
                await CashBoxSqlIntegration.Guard(cashGuardDb, f.CompanyId, cashReceipts[0].ResourceId, f.EquityId);
            await Expect("cashbox_account_already_linked", () =>
                f.Call(s => s.CashBox.CreateCashBox(f.CompanyId, f.Owner, Fixture.Key(),
                    cashRequest with { Code = "SQL-CASH-2" }, default)));
            Console.WriteLine("  SQL PASS CASH01 durable master card, audit, account uniqueness and concurrent idempotency");

            await using (var cashCountDb = f.Context())
                await CashCountSqlIntegration.Guard(cashCountDb, f.CompanyId, cashReceipts[0].ResourceId, f.BranchId);
            await using (var receiptDb = f.Context())
                await CashReceiptSqlIntegration.Guard(receiptDb, f.CompanyId, f.BranchId, cashReceipts[0].ResourceId);
            await using (var chequeDb = f.Context())
                await ChequeSqlIntegration.Guard(chequeDb, f.CompanyId, f.BranchId, f.CashId);
            await JournalDocumentTypeSqlIntegration.Guard(options.ConnectionString, f.CompanyId);
            await ApprovalRouteSqlIntegration.Guard(options.ConnectionString, f.CompanyId);
            await SecurityRoleSqlIntegration.Guard(options.ConnectionString, f.CompanyId);
            await SecurityRoleAssignmentSqlIntegration.Guard(options.ConnectionString, f.CompanyId);

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
            // SYS02-AC01: independent branch operational/reporting scope.
            // The first branch has posted journals; a new sibling has none.
            var emptyBranch = await f.Call(s => s.Branch.CreateBranch(f.CompanyId, f.Owner,
                Fixture.Key(), new("SYS02-AC01", "فرع قبول مستقل", "مدينة", "عنوان"), default));
            var mainBranchReport = await f.Call(s => s.Report.TrialBalance(f.CompanyId, f.Clerk,
                new(2026, 1, 1), new(2026, 12, 31), f.BranchId, default));
            var emptyBranchReport = await f.Call(s => s.Report.TrialBalance(f.CompanyId, f.Owner,
                new(2026, 1, 1), new(2026, 12, 31), emptyBranch.ResourceId, default));
            Ensure(mainBranchReport.BranchIds.Contains(f.BranchId) && mainBranchReport.TotalPeriodDebit > 0 &&
                emptyBranchReport.BranchIds.SequenceEqual(new[] { emptyBranch.ResourceId }) &&
                emptyBranchReport.TotalPeriodDebit == 0 && emptyBranchReport.TotalPeriodCredit == 0,
                "SYS02-AC01 branch-scoped reporting included a sibling's journals");
            await using (var branchDb = f.Context())
                Ensure(await branchDb.Set<Branch>().AnyAsync(x => x.CompanyId == f.CompanyId &&
                    x.Id == emptyBranch.ResourceId && x.Code == "SYS02-AC01") &&
                    await branchDb.Set<JournalEntry>().AnyAsync(x => x.CompanyId == f.CompanyId &&
                        x.BranchId == f.BranchId && x.State == JournalState.Posted) &&
                    !await branchDb.Set<JournalEntry>().AnyAsync(x => x.CompanyId == f.CompanyId &&
                        x.BranchId == emptyBranch.ResourceId),
                    "SYS02-AC01 SQL branch/journal tenant separation invalid");
            Console.WriteLine("  SQL PASS SYS02-AC01 independent branch operation and branch-scoped trial balance");


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
            // SYS01-OP01: database-backed listing must not disclose a freshly
            // provisioned company to users without membership in it.
            var ownerCompanies = await f.Call(s => s.Company.ListCompanies(f.Owner, default));
            var clerkCompanies = await f.Call(s => s.Company.ListCompanies(f.Clerk, default));
            Ensure(ownerCompanies.Any(x => x.Id == other) &&
                clerkCompanies.Any(x => x.Id == f.CompanyId) && clerkCompanies.All(x => x.Id != other),
                "SYS01-OP01 SQL company list crossed tenant membership boundaries");
            Console.WriteLine("  SQL PASS SYS01-OP01 company provisioning and membership-scoped listing");
            // SYS01-AC01: creating a legal entity bootstraps its own functional
            // currency/membership and an independent, tenant-scoped ledger. Both
            // entities may reuse account code 1 without sharing any postings.
            var primaryBook = await f.Call(s => s.Report.TrialBalance(f.CompanyId, f.Clerk,
                new(2026, 1, 1), new(2026, 12, 31), null, default));
            var secondaryBook = await f.Call(s => s.Report.TrialBalance(other, f.Owner,
                new(2026, 1, 1), new(2026, 12, 31), null, default));
            Ensure(primaryBook.CompanyId == f.CompanyId && primaryBook.TotalPeriodDebit > 0 &&
                secondaryBook.CompanyId == other && secondaryBook.Currency == "SAR" &&
                secondaryBook.TotalPeriodDebit == 0 && secondaryBook.TotalPeriodCredit == 0 &&
                secondaryBook.Rows.Count == 1 && secondaryBook.Rows[0].AccountId == otherAccount.ResourceId,
                "SYS01-AC01 legal entities did not retain independent financial books");
            await Expect("permission_denied", () => f.Call(s => s.Report.TrialBalance(other, f.Clerk,
                new(2026, 1, 1), new(2026, 12, 31), null, default)));
            await using (var tenantDb = f.Context())
                Ensure(await tenantDb.Set<Company>().AnyAsync(x => x.Id == other && x.Code == "OTHER") &&
                    await tenantDb.Set<Currency>().CountAsync(x => x.CompanyId == other && x.Code == "SAR") == 1 &&
                    await tenantDb.Set<Membership>().CountAsync(x => x.CompanyId == other && x.Subject == f.Owner.Subject) == 1 &&
                    await tenantDb.Set<JournalEntry>().AnyAsync(x => x.CompanyId == f.CompanyId) &&
                    !await tenantDb.Set<JournalEntry>().AnyAsync(x => x.CompanyId == other),
                    "SYS01-AC01 SQL company bootstrap or journal tenant separation invalid");
            Console.WriteLine("  SQL PASS SYS01-AC01 independent legal entity, functional currency, account book and denied cross-tenant read");
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
            await JournalClosedPeriodSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await JournalUnbalancedPostingSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
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
            await JournalHeaderFieldsSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Request());
            await JournalLineGuardAcceptance.RunSql(options.ConnectionString, f.CompanyId, f.Clerk, f.Owner, f.Request());
            await JournalReviewHistoryAcceptance.RunSql(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Owner, f.Request());
            await JournalReviewAuditFieldsSqlAcceptance.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await GeneralOpeningPrintSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await GeneralOpeningControlGuardSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner, f.Clerk, f.Request());
            await JournalPrintSqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Clerk, f.Approver, f.Request());
            await PeriodExecutionHistorySqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner, f.Approver);
            await PeriodReopenHistorySqlIntegration.Run(options.ConnectionString, f.CompanyId, f.Owner, f.Approver);
            await BalancedJournalLedgerSqlAcceptance.Run(options.ConnectionString,
                f.CompanyId, f.Clerk, f.Approver, f.Request());
            await PeriodPendingCloseSqlAcceptance.Run(options.ConnectionString,
                f.CompanyId, f.Owner, f.Clerk, f.Approver, f.Request());
            await PeriodReadyCloseSqlAcceptance.Run(options.ConnectionString,
                f.CompanyId, f.Owner, f.Clerk, f.Approver, f.Request());
            await PeriodConcurrentCloseSqlAcceptance.Run(options.ConnectionString,
                f.CompanyId, f.Owner, f.Approver);
            await PeriodUnbalancedCloseSqlAcceptance.Run(options.ConnectionString,
                f.CompanyId, f.Owner, f.Clerk, f.Approver, f.Request());
            await PeriodPendingInventoryCloseSqlAcceptance.Run(options.ConnectionString,
                f.CompanyId, f.BranchId, f.Owner, f.Clerk, f.Approver);
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
