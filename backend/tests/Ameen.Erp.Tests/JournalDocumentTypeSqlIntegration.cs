using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class JournalDocumentTypeSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "027_journal_document_types.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [JournalDocumentTypes] ([TestOnly] int NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting GL14 migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=27").AnyAsync(),
            "Failed GL14 migration saved version");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('JournalDocumentTypes') AND name='ApprovalRouteId'").SingleAsync() == 0,
            "Failed GL14 migration retained partial schema");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [JournalDocumentTypes]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=27").SingleAsync() == 1,
            "GL14 migration was not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('JournalDocumentTypes')").SingleAsync() == 15,
            "GL14 columns do not match field dictionary");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('JournalDocumentTypes')").SingleAsync() == 3,
            "GL14 company, branch or numbering FK missing");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id=OBJECT_ID('JournalDocumentTypes') AND name='IX_JournalDocumentTypes_CompanyId_Code' AND is_unique=1").SingleAsync() == 1,
            "GL14 code uniqueness missing");
        Ensure(!await db.Set<JournalDocumentType>().AnyAsync(), "GL14 migration invented configuration");
        Console.WriteLine("  SQL PASS GL14 026→027 atomic/idempotent schema, tenant FKs, code uniqueness, no seeded cards");
    }

    public static async Task Guard(string connection, Guid companyId)
    {
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        db.Add(new JournalDocumentType(companyId, null, "BLOCK", "غير مفعل",
            JournalDocumentOrigin.Manual, Guid.NewGuid(), Guid.NewGuid(), false,
            "sql-test", DateTimeOffset.UtcNow));
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("GL14 accepted an unvalidated SYS09 route");
        }
        catch (BusinessException ex) when (ex.Code == "document_type_approval_route_unavailable") { }
        await using var check = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        Ensure(!await check.Set<JournalDocumentType>().AnyAsync(),
            "GL14 guard leaked unvalidated card to SQL");
        Console.WriteLine("  SQL PASS GL14 refuses unvalidated approval route without writing a card");
    }

    private static void Ensure(bool ok, string why) { if (!ok) throw new Exception(why); }
}
