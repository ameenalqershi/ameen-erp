using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ameen.Erp.Tests;

public static class SchemaUpgradeIntegration
{
    public static async Task Run(ErpDbContext db)
    {
        // Seed old-schema rows, including a posted reversal, without using the new journal columns.
        var f = new Fixture(); await f.Initialize();
        var original = await f.Post();
        var reversal = await f.Service.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(),
            new(original.Version, Fixture.Date, "عكس قبل الترقية"), default);
        await f.Post(reversal);
        foreach (var type in new[] { AccountType.Liability, AccountType.Revenue, AccountType.Expense })
            f.Store.Add(new Account(f.CompanyId, "LEGACY-" + type, "حساب سابق", type, null, true, false, false, null,
                type == AccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit));
        var legacyAccounts = f.Store.All<Account>().ToArray();
        var legacyJournals = f.Store.All<JournalEntry>().ToArray();
        var rows = f.Store.All<Company>().Cast<Entity>().Concat(f.Store.All<Currency>()).Concat(f.Store.All<Branch>())
            .Concat(f.Store.All<FiscalYear>()).Concat(f.Store.All<FiscalPeriod>())
            .Concat(f.Store.All<Account>().OrderBy(x => x.ParentId is null ? 0 : 1)).Concat(legacyJournals)
            .Concat(legacyJournals.SelectMany(x => x.Lines));
        foreach (var entity in rows) await InsertLegacy(db, entity);
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "002_recurring_journals.sql"));

        // A conflicting object forces a mid-upgrade error; every preceding DDL change must roll back.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [RecurringJournalLines] ([TestOnly] int NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Number == 2714) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=2").AnyAsync(), "Failed upgrade saved version 2");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='RecurringJournalTemplates'").SingleAsync() == 0, "Failed upgrade left partial DDL");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [RecurringJournalLines]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        var accountScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "003_account_cards.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Account_NormalBalance] CHECK ([Type] >= 0)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(accountScript);
            throw new Exception("Conflicting account migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=3").AnyAsync(), "Failed upgrade saved version 3");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Accounts') AND name IN ('EnglishName','Notes','NormalBalance','Revision')").SingleAsync() == 0, "Failed account upgrade left partial columns");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Accounts] DROP CONSTRAINT [CK_Account_NormalBalance]");
        await db.Database.ExecuteSqlRawAsync(accountScript);
        await db.Database.ExecuteSqlRawAsync(accountScript);
        foreach (var current in await db.Set<Account>().Where(x => x.CompanyId == f.CompanyId).ToListAsync())
        {
            var previous = legacyAccounts.Single(x => x.Id == current.Id);
            Ensure(current.Version == previous.Version && current.Code == previous.Code && current.Type == previous.Type &&
                current.ParentId == previous.ParentId && current.IsActive == previous.IsActive && current.Revision == 1 && current.EnglishName is null && current.Notes is null,
                "Account upgrade changed legacy identity/classification");
            Ensure(current.NormalBalance == (previous.Type is AccountType.Asset or AccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit), "Legacy nature backfill differs");
        }
        var loaded = await db.Set<JournalEntry>().Where(x => x.CompanyId == f.CompanyId).ToListAsync();
        foreach (var previous in legacyJournals)
        {
            var current = loaded.Single(x => x.Id == previous.Id);
            Ensure(current.Source == (previous.ReversesJournalId is null ? JournalSource.Manual : JournalSource.Reversal), "Legacy source backfill differs");
            Ensure(current.Version == previous.Version && current.State == previous.State && current.AccountingDate == previous.AccountingDate, "Upgrade changed financial identity/state");
            Ensure(current.Lines.Sum(x => x.FunctionalDebit) == previous.Lines.Sum(x => x.FunctionalDebit) &&
                current.Lines.Sum(x => x.FunctionalCredit) == previous.Lines.Sum(x => x.FunctionalCredit), "Upgrade changed financial amounts");
        }
        Console.WriteLine("  SQL PASS atomic/idempotent schema 002→003 upgrade preserves account identity and backfills all five classifications");
        db.ChangeTracker.Clear();
        foreach (var table in new[] { "JournalLines", "Journals", "Accounts", "FiscalPeriods", "FiscalYears", "Branches", "Currencies" })
        {
            // Table identifiers come only from this fixed test list; values remain parameters.
            var sql = "DELETE FROM [" + table + "] WHERE [CompanyId]=@company";
            await db.Database.ExecuteSqlRawAsync(sql, new SqlParameter("@company", f.CompanyId));
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Companies] WHERE [Id]={f.CompanyId}");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 001→002 upgrade preserves posted amounts, versions and reversal provenance");
    }

    private static async Task InsertLegacy(ErpDbContext db, Entity entity)
    {
        var type = db.Model.FindEntityType(entity.GetType())!;
        var table = type.GetTableName()!;
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@table",
            new SqlParameter("@table", table)).ToListAsync();
        var tableId = StoreObjectIdentifier.Table(table, null);
        var properties = type.GetProperties().Where(p => columns.Contains(p.GetColumnName(tableId)!)).ToArray();
        var parameters = properties.Select((p, i) =>
        {
            var value = p.PropertyInfo!.GetValue(entity);
            value = p.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value;
            if (value is Enum e) value = Convert.ToInt32(e);
            return new SqlParameter("@p" + i, value ?? DBNull.Value);
        }).ToArray();
        var sql = $"INSERT INTO [{table}] (" + string.Join(",", properties.Select(p => "[" + p.GetColumnName(tableId) + "]")) +
            ") VALUES (" + string.Join(",", parameters.Select(p => p.ParameterName)) + ")";
        await db.Database.ExecuteSqlRawAsync(sql, parameters.Cast<object>());
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
