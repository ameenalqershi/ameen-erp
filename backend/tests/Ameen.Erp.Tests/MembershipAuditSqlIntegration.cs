using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Disposable SQL database only. This does not provision identity-provider users.
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
            throw new Exception("SYS06 audit migration unexpectedly accepted duplicate Revision");
        }
        catch (SqlException ex) when (ex.Number is 2705 or 2714) { }
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=47") == 0 &&
            await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Memberships') AND name IN ('CreatedBy','CreatedAt','LastModifiedAt')") == 0,
            "SYS06 failed migration retained partial columns or schema version");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Memberships DROP COLUMN [Revision]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=47") == 1 &&
            await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Memberships') AND name IN ('CreatedBy','CreatedAt','Revision','LastModifiedAt')") == 4,
            "SYS06 migration was incomplete or non-idempotent");
        Console.WriteLine("  SQL PASS SYS06-FG05 migration 047 atomic, idempotent, version 47 tracked");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, string ownerSubject)
    {
        var admin = await db.Set<Membership>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Subject == ownerSubject);
        Ensure(admin.CreatedBy == ownerSubject && admin.CreatedAt is not null &&
            admin.Revision == 1 && admin.LastModifiedAt == admin.CreatedAt,
            "SYS06 bootstrap membership audit metadata missing");
        var legacyId = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT dbo.Memberships ([Id],[CompanyId],[Subject],[GrantedPermissions],
                  [BranchIds],[AllBranches],[IsActive],[Version])
                VALUES ({legacyId},{companyId},{"sys06-legacy-unknown"},
                  {"[\"read\"]"},{"[]"},{true},{true},{Guid.NewGuid()})");
            var legacy = await db.Set<Membership>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == companyId && x.Id == legacyId);
            Ensure(legacy.CreatedBy is null && legacy.CreatedAt is null &&
                legacy.Revision is null && legacy.LastModifiedAt is null,
                "SYS06 migration fabricated old member creator, timestamp or version");
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.Memberships SET [CreatedBy]={"forged"},[Revision]={-1}
                    WHERE [CompanyId]={companyId} AND [Id]={legacyId}");
                throw new Exception("SYS06 accepted incomplete forged audit metadata");
            }
            catch (SqlException ex) when (ex.Number == 547) { }
            await tx.RollbackAsync();
        }
        Ensure(!await db.Set<Membership>().AnyAsync(x =>
            x.CompanyId == companyId && x.Id == legacyId),
            "SYS06 legacy audit guard left a test membership in the database");
        Console.WriteLine("  SQL PASS SYS06-FG05 legacy identities un-attributed, invalid audit rejected, isolated rollback");
    }

    private static async Task<int> Count(ErpDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
