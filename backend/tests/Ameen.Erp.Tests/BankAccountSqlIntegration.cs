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


    // CASH02 is a schema-only foundation. All statements below are executed
    // exclusively against SqlIntegration's disposable SQL database, bypassing
    // the intentionally fail-closed EF bank-account mutation guard.
    public static async Task Guard(ErpDbContext db, Guid companyId, Guid ledgerAccountId, Guid branchId)
    {
        var bankId = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [BankAccounts] ([CompanyId],[Id],[Version],[Code],[Name],[EnglishName],
                [IsActive],[Notes],[BankName],[AccountNumber],[Iban],[LedgerAccountId],
                [CurrencyCode],[BranchId],[CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
            VALUES ({companyId},{bankId},{Guid.NewGuid()},{"SQL-BANK"},
                {"حساب بنكي اختباري"},NULL,{true},NULL,{"بنك فحص"},
                {"0000012345"},{"GB82WEST12345698765432"},{ledgerAccountId},
                {"SAR"},{branchId},{"sql-schema-only"},{created},{1},{created})");
        var before = await db.Set<BankAccount>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == bankId);
        Ensure(before.AccountNumber == "0000012345" &&
            before.Iban == "GB82WEST12345698765432" &&
            before.CreatedAt == created && before.Revision == 1,
            "CASH02 SQL lost text leading zero, canonical IBAN or audit values");

        var invalidReference = Guid.NewGuid();
        await RejectSql("foreign ledger account", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankAccounts] SET [LedgerAccountId]={invalidReference} WHERE [CompanyId]={companyId} AND [Id]={bankId}"));
        await RejectSql("foreign branch", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankAccounts] SET [BranchId]={invalidReference} WHERE [CompanyId]={companyId} AND [Id]={bankId}"));
        await RejectSql("foreign currency", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankAccounts] SET [CurrencyCode]={"ZZZ"} WHERE [CompanyId]={companyId} AND [Id]={bankId}"));
        await RejectSql("non-positive revision", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankAccounts] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={bankId}"));
        await RejectSql("audit time before creation", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [BankAccounts] SET [LastModifiedAt]={created.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={bankId}"));
        await RejectSql("duplicate supplied IBAN", 2601,
            db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [BankAccounts] ([CompanyId],[Id],[Version],[Code],[Name],[EnglishName],
                    [IsActive],[Notes],[BankName],[AccountNumber],[Iban],[LedgerAccountId],
                    [CurrencyCode],[BranchId],[CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
                SELECT [CompanyId],{Guid.NewGuid()},{Guid.NewGuid()},{"SQL-BANK-2"},
                    [Name],[EnglishName],[IsActive],[Notes],[BankName],{"0000012346"},
                    [Iban],[LedgerAccountId],[CurrencyCode],[BranchId],
                    [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt]
                FROM [BankAccounts] WHERE [CompanyId]={companyId} AND [Id]={bankId}"));

        // A filtered IBAN index must accept more than one NULL in the same
        // company; the format/must-supply policy is deliberately not inferred.
        foreach (var code in new[] { "SQL-BANK-N1", "SQL-BANK-N2" })
        {
            var accountNumber = code == "SQL-BANK-N1" ? "0000012351" : "0000012352";
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [BankAccounts] ([CompanyId],[Id],[Version],[Code],[Name],[EnglishName],
                    [IsActive],[Notes],[BankName],[AccountNumber],[Iban],[LedgerAccountId],
                    [CurrencyCode],[BranchId],[CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
                SELECT [CompanyId],{Guid.NewGuid()},{Guid.NewGuid()},{code},
                    [Name],[EnglishName],[IsActive],[Notes],[BankName],{accountNumber},
                    NULL,[LedgerAccountId],[CurrencyCode],[BranchId],
                    [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt]
                FROM [BankAccounts] WHERE [CompanyId]={companyId} AND [Id]={bankId}");
        }

        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, before, "bank_account_policy_unavailable");

        var after = await db.Set<BankAccount>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == bankId);
        Ensure(after.Version == before.Version && after.Revision == before.Revision &&
            after.LedgerAccountId == before.LedgerAccountId &&
            after.BranchId == before.BranchId && after.CurrencyCode == before.CurrencyCode &&
            after.LastModifiedAt == before.LastModifiedAt,
            "Rejected CASH02 SQL updates mutated the bank-card foundation");
        Ensure(await db.Set<BankAccount>().CountAsync(x =>
            x.CompanyId == companyId && x.Code.StartsWith("SQL-BANK")) == 3,
            "CASH02 unique IBAN check rejected nullable rows or leaked a failed insert");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [BankAccounts] WHERE [CompanyId]={companyId} AND [Code] LIKE {"SQL-BANK%"}");
        Ensure(!await db.Set<BankAccount>().AnyAsync(x => x.CompanyId == companyId),
            "CASH02 disposable SQL fixtures were not cleaned up");
        Console.WriteLine("  SQL PASS CASH02 live tenant FKs, audit constraints, IBAN uniqueness/nullability and preserved zeros");
    }

    private static async Task RejectSql(string name, int expected, Task<int> action)
    {
        try { await action; }
        catch (SqlException ex) when (ex.Number == expected ||
            (expected == 2601 && ex.Number == 2627)) { return; }
        throw new Exception("CASH02 SQL constraint failed to reject: " + name);
    }

    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
