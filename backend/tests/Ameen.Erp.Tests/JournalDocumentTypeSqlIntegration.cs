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
        var columnShapes = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT([name], '|', TYPE_NAME([user_type_id]), '|', [max_length], '|', [is_nullable]) AS [Value] " +
            "FROM sys.columns WHERE [object_id]=OBJECT_ID('dbo.JournalDocumentTypes') ORDER BY [column_id]")
            .ToListAsync();
        var expectedColumnShapes = new[]
        {
            "CompanyId|uniqueidentifier|16|0",
            "Id|uniqueidentifier|16|0",
            "Version|uniqueidentifier|16|0",
            "BranchId|uniqueidentifier|16|1",
            "IsActive|bit|1|0",
            "Code|nvarchar|64|0",
            "Name|nvarchar|400|0",
            "Origin|int|4|0",
            "NumberSeriesId|uniqueidentifier|16|0",
            "ApprovalRouteId|uniqueidentifier|16|0",
            "RequiresAttachment|bit|1|0",
            "CreatedBy|nvarchar|400|0",
            "CreatedAt|datetimeoffset|10|0",
            "Revision|int|4|0",
            "LastModifiedAt|datetimeoffset|10|0"
        };
        Ensure(columnShapes.SequenceEqual(expectedColumnShapes),
            "GL14 SQL 027 column names, SQL types, lengths or nullability diverge from the field contract: " +
            string.Join(", ", columnShapes));
        Ensure(await db.Database.SqlQueryRaw<string>(
            "SELECT [collation_name] AS [Value] FROM sys.columns " +
            "WHERE [object_id]=OBJECT_ID('dbo.JournalDocumentTypes') AND [name]='Code'").SingleAsync()
                == "Latin1_General_100_BIN2",
            "GL14 code must retain exact binary comparison and leading-zero semantics");
        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.check_constraints " +
            "WHERE [parent_object_id]=OBJECT_ID('dbo.JournalDocumentTypes') ORDER BY [name]")
            .ToListAsync();
        Ensure(checks.SequenceEqual(new[]
        {
            "CK_JournalDocumentType_Origin", "CK_JournalDocumentType_Revision"
        }), "GL14 SQL 027 source/audit check constraints diverge from the domain contract");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('JournalDocumentTypes')").SingleAsync() == 3,
            "GL14 company, branch or numbering FK missing");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id=OBJECT_ID('JournalDocumentTypes') AND name='IX_JournalDocumentTypes_CompanyId_Code' AND is_unique=1").SingleAsync() == 1,
            "GL14 code uniqueness missing");
        Ensure(!await db.Set<JournalDocumentType>().AnyAsync(), "GL14 migration invented configuration");
        Console.WriteLine("  SQL PASS GL14-FG01/FG03: exact 15-column shape, optional tenant branch, binary code, required attachment bit, audit checks, FKs, uniqueness and no seeded cards");
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
