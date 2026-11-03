using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class DataImportSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script=await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,"database","041_data_import_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(41,SYSUTCDATETIME())");
        await Reject("unknown version owner", db.Database.ExecuteSqlRawAsync(script),51002);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE 'DataImport%'")==0,
            "SYS13 version collision created tables");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.__AmeenSchemaVersions WHERE [Version]=41");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE DataImportFieldMappings([Collision] int NOT NULL)");
        await Reject("schema DDL collision",db.Database.ExecuteSqlRawAsync(script),2714);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=41")==0 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='DataImportBatches'")==0,
            "SYS13 failed upgrade leaked version or header");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE DataImportFieldMappings");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=41")==1 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('DataImportBatches')")==13 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('DataImportFieldMappings')")==7 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('DataImportBatches'),OBJECT_ID('DataImportFieldMappings'))")==4,
            "SYS13 columns, version or tenant references missing");
        Ensure(!await db.Set<DataImportBatch>().AnyAsync() &&
            !await db.Set<DataImportFieldMapping>().AnyAsync(),
            "SYS13 schema seeded an unauthorized import");
        var denied=new DataImportBatch(Guid.NewGuid(),null,"test-only","kind","file",
            new DateOnly(2026,10,31),"duplicate-policy","sql-test",
            DateTimeOffset.UtcNow,new[] { ("source","target",(string?)null) });
        db.Add(denied);
        try { await db.SaveChangesAsync(); throw new Exception("SYS13 EF accepted unapproved import"); }
        catch(BusinessException ex) when(ex.Code=="data_import_policy_unavailable") { }
        db.ChangeTracker.Clear();
        Console.WriteLine("  SQL PASS SYS13-01 041 schema owner, rollback, replay, tenant keys and EF fail-closed");
    }

    public static async Task Guard(ErpDbContext db,Guid cid,Guid branch)
    {
        var id=Guid.NewGuid();var lineId=Guid.NewGuid();
        var now=new DateTimeOffset(2026,11,3,12,0,0,TimeSpan.Zero);
        var cutoff=new DateTime(2026,10,31);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT DataImportBatches
              (CompanyId,Id,Version,BranchId,StatusCode,DataKindCode,FileIdentifier,
               CutoffDate,DuplicatePolicyCode,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES ({cid},{id},{Guid.NewGuid()},{branch},{"test-only"},{"kind"},
                {"file-id"},{cutoff},{"unapproved-policy"},{"sql-test"},{now},{1},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT DataImportFieldMappings
              (CompanyId,Id,Version,BatchId,SourceColumn,TargetField,DefaultValue)
            VALUES ({cid},{lineId},{Guid.NewGuid()},{id},{"source"},{"target"},{"0"})");
        db.ChangeTracker.Clear();
        var stored=await db.Set<DataImportBatch>().AsNoTracking().Include(x=>x.Mappings)
            .SingleAsync(x=>x.CompanyId==cid && x.Id==id);
        Ensure(stored.BranchId==branch && stored.StatusCode=="test-only" &&
            stored.DataKindCode=="kind" && stored.FileIdentifier=="file-id" &&
            stored.CutoffDate==new DateOnly(2026,10,31) &&
            stored.DuplicatePolicyCode=="unapproved-policy" &&
            stored.CreatedBy=="sql-test" && stored.CreatedAt==now &&
            stored.LastModifiedAt==now && stored.Revision==1 &&
            stored.Mappings.Count==1 && stored.Mappings[0].CompanyId==cid &&
            stored.Mappings[0].SourceColumn=="source" &&
            stored.Mappings[0].TargetField=="target" && stored.Mappings[0].DefaultValue=="0",
            "SYS13 SQL/EF source mapping diverged");
        await Reject("other tenant branch",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE DataImportBatches SET BranchId={Guid.NewGuid()} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("other tenant import",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE DataImportFieldMappings SET BatchId={Guid.NewGuid()} WHERE CompanyId={cid} AND Id={lineId}"),547);
        await Reject("revision underflow",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE DataImportBatches SET Revision={0} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("audit rollback",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE DataImportBatches SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={cid} AND Id={id}"),547);
        Console.WriteLine("  SQL PASS SYS13-01 tenant FK, mapped field/audit roundtrip and denied invalid references");
    }

    private static Task<int> Count(ErpDbContext db,string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task Reject(string name,Task op,params int[] codes)
    {
        try { await op; throw new Exception("SYS13 accepted "+name); }
        catch(SqlException e) when(e.Errors.Cast<SqlError>().Any(x=>codes.Contains(x.Number))) { }
    }
    private static void Ensure(bool ok,string message)
    {
        if(!ok) throw new Exception(message);
    }
}
