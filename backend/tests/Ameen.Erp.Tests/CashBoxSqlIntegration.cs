using Ameen.Erp.Infrastructure;
using Ameen.Erp.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Runs only against the disposable SQL database created by SqlIntegration.Run.
public static class CashBoxSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "031_cashbox_cards.sql"));

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [CashBoxes] ([Conflict] int NOT NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting CASH01 migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }

        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=31").AnyAsync(),
            "Failed CASH01 migration recorded a version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [CashBoxes]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);

        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=31").SingleAsync() == 1,
            "CASH01 upgrade is not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('CashBoxes')").SingleAsync() == 5,
            "CASH01 tenant-scoped relations are missing");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('CashBoxes')").SingleAsync() == 3,
            "CASH01 monetary/revision/audit-time constraints are missing");
        // CASH01-CTRL01: every reference FK must include CompanyId, not just a
        // globally unique-looking Id. This protects tenant isolation at SQL level.
        Ensure(await db.Database.SqlQueryRaw<int>(@"SELECT COUNT(*) AS [Value]
            FROM sys.foreign_keys fk
            WHERE fk.parent_object_id = OBJECT_ID(N'dbo.CashBoxes')
              AND fk.name <> N'FK_CashBox_Company'
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id = fk.object_id) = 2
              AND EXISTS (SELECT 1
                          FROM sys.foreign_key_columns fc
                          JOIN sys.columns c ON c.object_id = fc.parent_object_id
                              AND c.column_id = fc.parent_column_id
                          WHERE fc.constraint_object_id = fk.object_id
                            AND c.name = N'CompanyId')").SingleAsync() == 4,
            "CASH01 tenant-scoped composite account/branch/custodian/currency FKs are missing");
        // Schema-level verification of the precise separation dimensions in the
        // original analysis: company + account + branch + currency.
        Ensure(await db.Database.SqlQueryRaw<int>(@"SELECT COUNT(*) AS [Value]
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.CashBoxes')
              AND i.name = N'IX_CashBoxes_CompanyId_AccountId_BranchId_CurrencyCode'
              AND i.is_unique = 1
              AND (SELECT COUNT(*) FROM sys.index_columns ic
                   WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                     AND ic.is_included_column=0 AND ic.key_ordinal > 0) = 4
              AND NOT EXISTS (
                  SELECT 1 FROM (SELECT 1 AS ordinal, N'CompanyId' AS col
                        UNION ALL SELECT 2, N'AccountId'
                        UNION ALL SELECT 3, N'BranchId'
                        UNION ALL SELECT 4, N'CurrencyCode') expected
                  WHERE NOT EXISTS (
                      SELECT 1 FROM sys.index_columns ic
                      JOIN sys.columns c ON c.object_id=ic.object_id
                          AND c.column_id=ic.column_id
                      WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                        AND ic.key_ordinal=expected.ordinal AND c.name=expected.col))
            ").SingleAsync() == 1,
            "CASH01 duplicate-account guard must be uniquely indexed by tenant/account/branch/currency");

        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('CashBoxes')").SingleAsync() == 17,
            "CASH01 original fields are not fully persisted");

        Console.WriteLine("  SQL PASS CASH01 atomic/idempotent schema 031, tenant FKs and field constraints");
    }


    // CASH01 SQL acceptance against disposable test data: the SQL constraints
    // must reject writes even when the public API/domain layer is bypassed.
    public static async Task Guard(ErpDbContext db, Guid companyId, Guid cashBoxId)
    {
        var before = await db.Set<CashBox>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == cashBoxId);
        var auditCount = await db.Set<AuditEvent>().CountAsync(x =>
            x.CompanyId == companyId && x.ResourceId == cashBoxId);
        var unknown = Guid.NewGuid();

        await RejectSql("account tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashBoxes] SET [AccountId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));
        await RejectSql("branch tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashBoxes] SET [BranchId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));
        await RejectSql("custodian tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashBoxes] SET [CustodianMembershipId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));
        await RejectSql("currency tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashBoxes] SET [CurrencyCode]={"ZZZ"} WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));
        await RejectSql("negative balance limit check", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashBoxes] SET [BalanceLimit]={-1m} WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));
        await RejectSql("revision check", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashBoxes] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));
        // A different code with the same account/branch/currency must be
        // rejected by the actual unique SQL index, not only by a prior query.
        await RejectSql("account/branch/currency unique index", 2601,
            db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [CashBoxes] ([CompanyId],[Id],[Version],[Code],[Name],
                    [EnglishName],[IsActive],[Notes],[AccountId],[BranchId],
                    [CustodianMembershipId],[CurrencyCode],[BalanceLimit],
                    [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
                SELECT [CompanyId],{Guid.NewGuid()},{Guid.NewGuid()},{"SQL-CASH-DUP"},
                    [Name],[EnglishName],[IsActive],[Notes],[AccountId],[BranchId],
                    [CustodianMembershipId],[CurrencyCode],[BalanceLimit],
                    [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt]
                FROM [CashBoxes] WHERE [CompanyId]={companyId} AND [Id]={cashBoxId}"));

        var after = await db.Set<CashBox>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == cashBoxId);
        Ensure(after.AccountId == before.AccountId && after.BranchId == before.BranchId &&
            after.CustodianMembershipId == before.CustodianMembershipId &&
            after.CurrencyCode == before.CurrencyCode && after.BalanceLimit == before.BalanceLimit &&
            after.Version == before.Version && after.Revision == before.Revision &&
            after.LastModifiedAt == before.LastModifiedAt, "Rejected CASH01 SQL writes mutated the original card");
        Ensure(await db.Set<CashBox>().CountAsync(x => x.CompanyId == companyId) == 1,
            "Rejected CASH01 SQL insert left an additional card");
        Ensure(await db.Set<AuditEvent>().CountAsync(x =>
            x.CompanyId == companyId && x.ResourceId == cashBoxId) == auditCount,
            "Rejected CASH01 SQL writes leaked audit events");
        Console.WriteLine("  SQL PASS CASH01 live tenant FKs, amount/revision constraints, uniqueness and atomic rejection");
    }

    private static async Task RejectSql(string name, int expectedNumber, Task<int> action)
    {
        try { await action; }
        catch (SqlException ex) when (ex.Number == expectedNumber ||
            (expectedNumber == 2601 && ex.Number == 2627)) { return; }
        throw new Exception("CASH01 SQL constraint failed to reject: " + name);
    }

    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
