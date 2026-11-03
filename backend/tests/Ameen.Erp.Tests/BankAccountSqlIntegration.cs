using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// CASH02 schema foundation on the disposable SQL database used by SqlIntegration.Run.
public static class BankAccountSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "032_bank_account_cards.sql"));

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [BankAccounts] ([Conflict] int NOT NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting CASH02 migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=32").AnyAsync(),
            "Failed CASH02 upgrade recorded schema version 32");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [BankAccounts]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);

        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=32").SingleAsync() == 1,
            "CASH02 bank schema migration was not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('BankAccounts')").SingleAsync() == 18,
            "CASH02 missing SQL persistence fields");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('BankAccounts')").SingleAsync() == 4,
            "CASH02 tenant-aware foreign keys are incomplete");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('BankAccounts')").SingleAsync() == 3,
            "CASH02 revision, timestamp or IBAN constraints are missing");
        // All bank references remain scoped to the current tenant in SQL.
        Ensure(await db.Database.SqlQueryRaw<int>(@"SELECT COUNT(*) AS [Value]
            FROM sys.foreign_keys fk
            WHERE fk.parent_object_id = OBJECT_ID(N'dbo.BankAccounts')
              AND fk.name <> N'FK_BankAccount_Company'
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id = fk.object_id) = 2
              AND EXISTS (
                  SELECT 1 FROM sys.foreign_key_columns fc
                  JOIN sys.columns c ON c.object_id=fc.parent_object_id
                      AND c.column_id=fc.parent_column_id
                  WHERE fc.constraint_object_id=fk.object_id
                    AND c.name=N'CompanyId')").SingleAsync() == 3,
            "CASH02 bank card must use company-scoped ledger/currency/branch FKs");
        // An optional IBAN is unique only when supplied; a globally unique
        // index would incorrectly cross tenant boundaries or disallow NULL.
        Ensure(await db.Database.SqlQueryRaw<int>(@"SELECT COUNT(*) AS [Value]
            FROM sys.indexes i
            WHERE i.object_id=OBJECT_ID(N'dbo.BankAccounts')
              AND i.name=N'IX_BankAccounts_CompanyId_Iban'
              AND i.is_unique=1 AND i.has_filter=1
              AND i.filter_definition LIKE N'%Iban%'
              AND i.filter_definition LIKE N'%NOT NULL%'
              AND (SELECT COUNT(*) FROM sys.index_columns ic
                   WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                     AND ic.is_included_column=0 AND ic.key_ordinal>0)=2
              AND NOT EXISTS (
                  SELECT 1 FROM (
                      SELECT 1 AS ordinal, N'CompanyId' AS col
                      UNION ALL SELECT 2, N'Iban') expected
                  WHERE NOT EXISTS (
                      SELECT 1 FROM sys.index_columns ic
                      JOIN sys.columns c ON c.object_id=ic.object_id
                          AND c.column_id=ic.column_id
                      WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                        AND ic.key_ordinal=expected.ordinal AND c.name=expected.col))
            ").SingleAsync() == 1,
            "CASH02 optional IBAN must have a tenant-scoped filtered unique index");

        Console.WriteLine("  SQL PASS CASH02-01 atomic/idempotent 032 schema, 18 columns, tenant FKs and constraints");

        var companyId = Guid.NewGuid();
        var bank = new BankAccount(companyId, "BANK-A", "حساب", "بنك", "0000123",
            null, Guid.NewGuid(), "SAR", Guid.NewGuid(), "test-actor",
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        db.Add(bank);
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("Unapproved CASH02 master-card mutation reached SQL");
        }
        catch (BusinessException ex) when (ex.Code == "bank_account_policy_unavailable") { }
        db.Entry(bank).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH02-01 EF rejects unapproved bank-card writes");
    }

    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
