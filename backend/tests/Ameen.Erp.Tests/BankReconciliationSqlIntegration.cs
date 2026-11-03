using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// CASH10-01 real SQL acceptance on disposable database only.
public static class BankReconciliationSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "040_bank_reconciliations_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE [BankReconciliationLines] ([Conflict] int NOT NULL)");
        await RejectSql("atomic failed migration", 2714,
            db.Database.ExecuteSqlRawAsync(script));
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=40").AnyAsync(),
            "CASH10 failed upgrade recorded version");
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT 1 AS [Value] FROM sys.tables WHERE name=N'BankReconciliations'").AnyAsync(),
            "CASH10 failed upgrade leaked header table");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [BankReconciliationLines]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=40").SingleAsync() == 1,
            "CASH10 migration not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.BankReconciliations')").SingleAsync() == 15 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.BankReconciliationLines')").SingleAsync() == 8,
            "CASH10 original header/line or technical field counts differ");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.parent_object_id IN (OBJECT_ID(N'dbo.BankReconciliations'),
                                         OBJECT_ID(N'dbo.BankReconciliationLines'))
              AND fk.name NOT IN (N'FK_BankReconciliation_Company', N'FK_BankReconciliationLine_Company')
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id=fk.object_id)=2
              AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                  JOIN sys.columns col ON col.object_id=fc.parent_object_id
                                        AND col.column_id=fc.parent_column_id
                  WHERE fc.constraint_object_id=fk.object_id AND col.name=N'CompanyId')
          ").SingleAsync() == 3, "CASH10 tenant-scoped FKs missing");

        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var test = new BankReconciliation(Guid.NewGuid(), null, "Unreviewed",
            Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3),
            -1m, 2m, null, "tester", now);
        db.Add(test);
        await RejectDomain("bank_reconciliation_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(test).State = EntityState.Detached;
        var line = new BankReconciliationLine(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), null, 1m, null);
        db.Add(line);
        await RejectDomain("bank_reconciliation_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(line).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH10-01 atomic/idempotent schema 040, tenant FKs, EF fail closed");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId)
    {
        var bankId = await db.Set<BankAccount>().Where(x => x.CompanyId == companyId)
            .Select(x => x.Id).FirstOrDefaultAsync();
        // The disposable SqlFixture does not create a bank operational card:
        // create a structural fixture directly through SQL, with valid tenant
        // account/branch/currency. Never instantiate the prohibited EF workflow.
        var createdBankFixture = bankId == Guid.Empty;
        if (createdBankFixture)
        {
            bankId = Guid.NewGuid();
            var accountId = await db.Set<Account>().Where(x => x.CompanyId == companyId)
                .Select(x => x.Id).FirstAsync();
            var nowBank = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [BankAccounts] ([CompanyId],[Id],[Version],[Code],
                   [Name],[EnglishName],[IsActive],[Notes],[BankName],[AccountNumber],
                   [Iban],[LedgerAccountId],[CurrencyCode],[BranchId],
                   [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
                VALUES ({companyId},{bankId},{Guid.NewGuid()},{"B-RECON"},
                   {"اختبار بنك"},{"Test"},{true},{(string?)null},{"Test Bank"},{"0000789"},
                   {(string?)null},{accountId},{"SAR"},{branchId},
                   {"tester"},{nowBank},{1},{nowBank})");
        }

        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var reconciliationId = Guid.NewGuid();
        var opening = new SqlParameter("@statementOpening", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = -52.000001m };
        var closing = new SqlParameter("@statementClosing", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 100.123456m };
        var matched = new SqlParameter("@matched", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 10.000001m };
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [BankReconciliations] ([CompanyId],[Id],[Version],
                [BranchId],[State],[BankAccountId],[StatementFrom],[StatementTo],
                [OpeningBalance],[ClosingBalance],[StatementFileId],
                [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
            VALUES ({companyId},{reconciliationId},{Guid.NewGuid()},
                {branchId},{"Unreviewed"},{bankId},{start},{end},
                {opening},{closing},{"statement-0001"},
                {"tester"},{now},{1},{now})");
        var saved = await db.Set<BankReconciliation>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == reconciliationId);
        Ensure(saved.OpeningBalance == -52.000001m &&
            saved.ClosingBalance == 100.123456m && saved.StatementFileId == "statement-0001" &&
            saved.StatementTo == end && saved.Revision == 1 && saved.BankAccountId == bankId,
            "CASH10 header money, scope and audit round-trip failed");

        var bankMovement = Guid.NewGuid(); // no approved movement master source.
        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [BankReconciliationLines] ([CompanyId],[Id],[Version],
                [ReconciliationId],[BankMovementId],[LedgerMovementId],
                [MatchedAmount],[VarianceReason])
            VALUES ({companyId},{line1},{Guid.NewGuid()},{reconciliationId},
                {bankMovement},{Guid.NewGuid()},{matched},{"difference"})");
        // CASH10 requires partial/many-to-one matching; never invent uniqueness
        // on bank movement in this structural-only foundation.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [BankReconciliationLines] ([CompanyId],[Id],[Version],
                [ReconciliationId],[BankMovementId],[LedgerMovementId],
                [MatchedAmount],[VarianceReason])
            VALUES ({companyId},{line2},{Guid.NewGuid()},{reconciliationId},
                {bankMovement},{(Guid?)null},{0m},{(string?)null})");
        Ensure(await db.Set<BankReconciliationLine>().Where(x =>
            x.CompanyId == companyId && x.ReconciliationId == reconciliationId)
            .SumAsync(x => x.MatchedAmount) == 10.000001m,
            "CASH10 partial line preservation failed");
        await RejectSql("unknown bank account", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliations] SET [BankAccountId]={Guid.NewGuid()} WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}"));
        await RejectSql("unknown branch", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliations] SET [BranchId]={Guid.NewGuid()} WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}"));
        await RejectSql("reversed statement range", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliations] SET [StatementTo]={start.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}"));
        await RejectSql("revision must be positive", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliations] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}"));
        await RejectSql("blank state", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliations] SET [State]={" "} WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}"));
        await RejectSql("negative matched amount", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliationLines] SET [MatchedAmount]={-1m} WHERE [CompanyId]={companyId} AND [Id]={line1}"));
        await RejectSql("unknown reconciliation", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankReconciliationLines] SET [ReconciliationId]={Guid.NewGuid()} WHERE [CompanyId]={companyId} AND [Id]={line1}"));
        await RejectSql("parent cannot delete with rows", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [BankReconciliations] WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}"));
        Ensure((await db.Set<BankReconciliation>().AsNoTracking().SingleAsync(
            x => x.CompanyId == companyId && x.Id == reconciliationId)).Revision == 1 &&
            (await db.Set<BankReconciliationLine>().AsNoTracking().SingleAsync(
            x => x.CompanyId == companyId && x.Id == line1)).MatchedAmount == 10.000001m,
            "CASH10 invalid writes leaked persistence");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [BankReconciliationLines] WHERE [CompanyId]={companyId} AND [ReconciliationId]={reconciliationId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [BankReconciliations] WHERE [CompanyId]={companyId} AND [Id]={reconciliationId}");
        // Do not remove pre-existing bank cards; clean up only our fixture.
        if (createdBankFixture)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [BankAccounts] WHERE [CompanyId]={companyId} AND [Id]={bankId}");
        Console.WriteLine("  SQL PASS CASH10-01 signed statement balance, partial lines, tenant FKs and rejects");
    }

    private static async Task RejectSql(string scenario, int code, Task<int> work)
    {
        try { await work; }
        catch (SqlException ex) when (ex.Number == code ||
            ex.Errors.Cast<SqlError>().Any(x => x.Number == code)) { return; }
        throw new Exception("CASH10 SQL must reject: " + scenario);
    }

    private static async Task RejectDomain(string code, Func<Task<int>> work)
    {
        try { await work(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH10 EF must reject: " + code);
    }

    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
    }
}
