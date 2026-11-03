using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// SYS06-FG05: upgrade only disposable integration SQL databases.
public static class MembershipAuditSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "047_membership_audit_metadata.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Memberships ADD [Revision] int NULL");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("SYS06 audit migration accepted a duplicate revision column");
        }
        catch (SqlException ex) when (ex.Number is 2705 or 2714) { }
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=47") == 0 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Memberships') AND name IN ('CreatedBy','CreatedAt','LastModifiedAt')") == 0,
            "SYS06 failed audit migration left partial data definition or recorded version");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Memberships DROP COLUMN [Revision]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=47") == 1 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Memberships') AND name IN ('CreatedBy','CreatedAt','Revision','LastModifiedAt')") == 4,
            "SYS06 audit migration is incomplete or not idempotent");
        Console.WriteLine("  SQL PASS SYS06-FG05 migration 047 atomic, idempotent and versioned");
    }
    public static async Task Guard(ErpDbContext db, Guid cid)
    {
        var current = await db.Set<Membership>().AsNoTracking()
            .FirstAsync(x => x.CompanyId == cid);
        Ensure(current.CreatedBy is not null && current.CreatedAt is not null &&
            current.Revision == 1 && current.LastModifiedAt == current.CreatedAt,
            "SYS06 existing newly granted membership lacks server audit metadata");
        var legacyId = Guid.NewGuid();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT dbo.Memberships ([Id],[CompanyId],[Subject],[GrantedPermissions],
                    [BranchIds],[AllBranches],[IsActive],[Version])
                VALUES ({legacyId},{cid},{"sys06-old-unattributed"},
                    {"[\"read\"]"},{"[]"},{true},{true},{Guid.NewGuid()})");
            var legacy = await db.Set<Membership>().AsNoTracking()
                .SingleAsync(x => x.CompanyId == cid && x.Id == legacyId);
            Ensure(legacy.CreatedBy is null && legacy.CreatedAt is null &&
                legacy.Revision is null && legacy.LastModifiedAt is null,
                "SYS06 migration fabricated historical member actor/time/revision");
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.Memberships SET [CreatedBy]={"forged"},[Revision]={-1}
                    WHERE [CompanyId]={cid} AND [Id]={legacyId}");
                throw new Exception("SYS06 SQL accepted forged partial audit metadata");
            }
            catch (SqlException ex) when (ex.Number == 547) { }
            await transaction.RollbackAsync();
        }
        Ensure(!await db.Set<Membership>().AnyAsync(x => x.CompanyId == cid && x.Id == legacyId),
            "SYS06 test left behind a forged legacy member");
        Console.WriteLine("  SQL PASS SYS06-FG05 real new audit metadata, unknown legacy history, invalid metadata blocked");
    }
    private static Task<int> Count(ErpDbContext db, string sql) => db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
