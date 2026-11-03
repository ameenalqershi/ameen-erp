using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class PrintTemplateSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var sql=await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,"database","042_print_template_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(42,SYSUTCDATETIME())");
        await Reject("unknown schema owner",db.Database.ExecuteSqlRawAsync(sql),51002);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='PrintTemplates'")==0,
            "SYS12 version collision created a table");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.__AmeenSchemaVersions WHERE [Version]=42");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE PrintTemplates([Collision] int NOT NULL)");
        await Reject("DDL conflict",db.Database.ExecuteSqlRawAsync(sql),2714);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=42")==0,
            "SYS12 failed migration saved schema version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE PrintTemplates");
        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync(sql);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=42")==1 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('PrintTemplates')")==15 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('PrintTemplates')")==2,
            "SYS12 required source fields or tenant FKs missing");
        Ensure(!await db.Set<PrintTemplate>().AnyAsync(),
            "SYS12 migration created an unauthorized template");

        var denied = new PrintTemplate(Guid.NewGuid(),null,"test-draft","doc","lang",
            "paper","قالب","V1",true,"actor",DateTimeOffset.UtcNow);
        db.Add(denied);
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("SYS12 unapproved EF write accepted");
        }
        catch(BusinessException e) when(e.Code=="print_template_policy_unavailable") { }
        db.Entry(denied).State=EntityState.Detached;
        Console.WriteLine("  SQL PASS SYS12-01 042 schema rollback, replay, version collision and guarded writes");
    }

    public static async Task Guard(ErpDbContext db,Guid cid,Guid branch)
    {
        var id=Guid.NewGuid();
        var now=new DateTimeOffset(2026,11,3,12,0,0,TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT PrintTemplates
             (CompanyId,Id,Version,BranchId,StatusCode,DocumentTypeCode,LanguageCode,
              PaperSizeCode,Name,VersionCode,IsDraft,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({cid},{id},{Guid.NewGuid()},{branch},{"test-draft"},{"unapproved-doc"},
                   {"unapproved-lang"},{"unapproved-paper"},{"قالب تجريبي"},{"V1"},
                   {true},{"sql-test"},{now},{1},{now})");
        db.ChangeTracker.Clear();
        var stored=await db.Set<PrintTemplate>().AsNoTracking()
            .SingleAsync(x=>x.CompanyId==cid && x.Id==id);
        Ensure(stored.BranchId==branch && stored.StatusCode=="test-draft" &&
            stored.DocumentTypeCode=="unapproved-doc" &&
            stored.LanguageCode=="unapproved-lang" &&
            stored.PaperSizeCode=="unapproved-paper" &&
            stored.Name=="قالب تجريبي" && stored.VersionCode=="V1" &&
            stored.IsDraft && stored.CreatedBy=="sql-test" &&
            stored.CreatedAt==now && stored.LastModifiedAt==now &&
            stored.Revision==1,
            "SYS12 SQL/EF mapping changed source fields");
        await Reject("foreign tenant branch",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PrintTemplates SET BranchId={Guid.NewGuid()} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("audit timestamp rollback",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PrintTemplates SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("invalid revision",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PrintTemplates SET Revision={0} WHERE CompanyId={cid} AND Id={id}"),547);
        Ensure(await db.Set<PrintTemplate>().AsNoTracking()
            .AnyAsync(x=>x.CompanyId==cid && x.Id==id && x.IsDraft && x.Revision==1),
            "SYS12 rejected SQL mutated draft or audit");
        Console.WriteLine("  SQL PASS SYS12-01 branch composite FK, audit guards and EF roundtrip");
    }

    private static Task<int> Count(ErpDbContext db,string query) =>
        db.Database.SqlQueryRaw<int>(query).SingleAsync();
    private static async Task Reject(string scope,Task work,params int[] errors)
    {
        try { await work; throw new Exception("SYS12 accepted "+scope); }
        catch(SqlException ex) when(ex.Errors.Cast<SqlError>().Any(x=>errors.Contains(x.Number))) { }
    }
    private static void Ensure(bool ok,string message)
    {
        if(!ok) throw new Exception(message);
    }
}
