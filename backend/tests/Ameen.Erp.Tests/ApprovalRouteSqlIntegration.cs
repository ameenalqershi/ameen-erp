using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class ApprovalRouteSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "028_approval_route_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [ApprovalRoutes] ([TestOnly] int NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting SYS09 migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=28").AnyAsync(),
            "Failed SYS09 migration recorded version");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='ApprovalRouteStages'").SingleAsync() == 0,
            "Failed SYS09 migration leaked stages table");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name='FK_JournalDocumentType_ApprovalRoute'").SingleAsync() == 0,
            "Failed SYS09 migration leaked GL14 FK");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [ApprovalRoutes]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=28").SingleAsync() == 1,
            "SYS09 migration was not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('ApprovalRoutes')").SingleAsync() == 14 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('ApprovalRouteStages')").SingleAsync() == 7,
            "SYS09 schema fields mismatch");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('ApprovalRoutes')").SingleAsync() == 3 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('ApprovalRouteStages')").SingleAsync() == 2 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name='FK_JournalDocumentType_ApprovalRoute'").SingleAsync() == 1,
            "SYS09/GL14 tenant FKs missing");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id=OBJECT_ID('ApprovalRouteStages') AND name='IX_ApprovalRouteStages_CompanyId_ApprovalRouteId_StageNumber' AND is_unique=1").SingleAsync() == 1,
            "SYS09 stage uniqueness missing");
        Ensure(!await db.Set<ApprovalRoute>().AnyAsync() && !await db.Set<ApprovalRouteStage>().AnyAsync(),
            "SYS09 migration invented routes or approver roles");
        Console.WriteLine("  SQL PASS SYS09 027→028 atomic/idempotent, tenant FKs, GL14 FK, no seeded roles");
    }

    public static async Task Guard(string connection, Guid companyId)
    {
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        var route = new ApprovalRoute(companyId, null, "journal", 0, null,
            Guid.NewGuid(), true, "sql-test", DateTimeOffset.UtcNow);
        route.AddStage(1, Guid.NewGuid(), 10, DateTimeOffset.UtcNow.AddSeconds(1));
        db.Add(route);
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("SYS09 accepted an unvalidated approver role");
        }
        catch (BusinessException ex) when (ex.Code == "approval_route_role_policy_unavailable") { }
        await using var check = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        Ensure(!await check.Set<ApprovalRoute>().AnyAsync() &&
            !await check.Set<ApprovalRouteStage>().AnyAsync(),
            "SYS09 guard leaked configuration to SQL");
        Console.WriteLine("  SQL PASS SYS09 refuses unvalidated roles without writing route/stages");
    }

    private static void Ensure(bool ok, string message) { if (!ok) throw new Exception(message); }
}
