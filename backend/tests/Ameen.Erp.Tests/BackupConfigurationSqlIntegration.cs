using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Executes exclusively on SqlIntegration's disposable database.
public static class BackupConfigurationSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var sql=await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,"database","042_backup_configuration_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(42,SYSUTCDATETIME())");
        await Reject("unowned version",db.Database.ExecuteSqlRawAsync(sql),51002);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='BackupJobConfigurations'")==0,
            "SYS15 wrong-owner migration created a table");
        await db.Database.ExecuteSqlRawAsync("DELETE dbo.__AmeenSchemaVersions WHERE [Version]=42");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE BackupJobConfigurations ([Collision] int NOT NULL)");
        await Reject("table collision",db.Database.ExecuteSqlRawAsync(sql),2714);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=42")==0,
            "SYS15 failed migration marked a version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE BackupJobConfigurations");
        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync(sql);
        Ensure(await Count(db,"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=42")==1 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('BackupJobConfigurations')")==15 &&
            await Count(db,"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('BackupJobConfigurations')")==2,
            "SYS15 replay, source fields or composite tenant reference missing");
        Ensure(!await db.Set<BackupJobConfiguration>().AnyAsync(),
            "SYS15 migration seeded an unapproved backup");
        var denied=new BackupJobConfiguration(Guid.NewGuid(),null,"test-only","test",
            "no-schedule","no-destination",3,true,null,"actor",DateTimeOffset.UtcNow);
        db.Add(denied);
        try { await db.SaveChangesAsync(); throw new Exception("SYS15 unapproved EF write persisted"); }
        catch(BusinessException ex) when(ex.Code=="backup_job_policy_unavailable") { }
        db.ChangeTracker.Clear();
        Console.WriteLine("  SQL PASS SYS15-01 042 migration collision, replay, rollback and EF write guard");
    }

    public static async Task Guard(ErpDbContext db,Guid cid,Guid branch)
    {
        var id=Guid.NewGuid();
        var now=new DateTimeOffset(2026,11,3,12,0,0,TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT BackupJobConfigurations
             (CompanyId,Id,Version,BranchId,StatusCode,Name,ScheduleText,Destination,
              RetentionPolicy,IsEncrypted,LastTestReference,CreatedBy,CreatedAt,
              Revision,LastModifiedAt)
            VALUES({cid},{id},{Guid.NewGuid()},{branch},{"test-only"},{"نسخة اختبارية"},
                   {"no-schedule"},{"no-destination"},{7},{true},{"test-run"},
                   {"sql-test"},{now},{1},{now})");
        db.ChangeTracker.Clear();
        var job=await db.Set<BackupJobConfiguration>().AsNoTracking()
            .SingleAsync(x=>x.CompanyId==cid && x.Id==id);
        Ensure(job.BranchId==branch && job.StatusCode=="test-only" &&
            job.Name=="نسخة اختبارية" && job.ScheduleText=="no-schedule" &&
            job.Destination=="no-destination" && job.RetentionPolicy==7 &&
            job.IsEncrypted && job.LastTestReference=="test-run" &&
            job.CreatedBy=="sql-test" && job.CreatedAt==now &&
            job.LastModifiedAt==now && job.Revision==1,
            "SYS15 SQL/EF backup metadata roundtrip failed");
        await Reject("foreign tenant branch",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE BackupJobConfigurations SET BranchId={Guid.NewGuid()} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("negative retention",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE BackupJobConfigurations SET RetentionPolicy={-1} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("version underflow",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE BackupJobConfigurations SET Revision={0} WHERE CompanyId={cid} AND Id={id}"),547);
        await Reject("audit rollback",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE BackupJobConfigurations SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={cid} AND Id={id}"),547);
        Console.WriteLine("  SQL PASS SYS15-01 tenant branch FK, retention and audit checks with EF mapping");
    }

    private static Task<int> Count(ErpDbContext db,string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task Reject(string label,Task operation,params int[] errorCodes)
    {
        try { await operation; throw new Exception("SYS15 accepted "+label); }
        catch(SqlException ex) when(ex.Errors.Cast<SqlError>().Any(x=>errorCodes.Contains(x.Number))) { }
    }
    private static void Ensure(bool ok,string message)
    {
        if(!ok) throw new Exception(message);
    }
}
