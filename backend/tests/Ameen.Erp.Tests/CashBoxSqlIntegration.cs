using Ameen.Erp.Infrastructure;
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
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('CashBoxes')").SingleAsync() == 17,
            "CASH01 original fields are not fully persisted");

        Console.WriteLine("  SQL PASS CASH01 atomic/idempotent schema 031, tenant FKs and field constraints");
    }

    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
