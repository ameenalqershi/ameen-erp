using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// SYS03-FG04 foundation: no invented creator or revision for pre-existing years.
// Every SQL check here runs against SqlIntegration's disposable test database.
public static class FiscalYearAuditSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "046_fiscal_year_audit_metadata.sql"));
        // A column collision must roll back *all* schema changes and version bookkeeping.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.FiscalYears ADD [Revision] int NULL");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("SYS03 audit migration unexpectedly accepted duplicate Revision");
        }
        catch (SqlException ex) when (ex.Number is 2705 or 2714) { }
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=46") == 0 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.FiscalYears') AND name IN ('CreatedBy','CreatedAt','LastModifiedAt')") == 0,
            "SYS03 failed audit migration left a partial schema or version");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.FiscalYears DROP COLUMN [Revision]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=46") == 1 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.FiscalYears') AND name IN ('CreatedBy','CreatedAt','Revision','LastModifiedAt')") == 4,
            "SYS03 audit migration is not complete or idempotent");
        Console.WriteLine("  SQL PASS SYS03-FG04 migration 046 atomic, idempotent and schema version tracked");
    }
    public static async Task Guard(ErpDbContext db, Guid companyId)
    {
        // A year created under the old contract has unknown metadata; do not guess it.
        var old = await db.Set<FiscalYear>().AsNoTracking().FirstAsync(x => x.CompanyId == companyId);
        var legacyId = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT dbo.FiscalYears ([Id],[CompanyId],[Code],[StartsOn],[EndsOn],[Version])
                VALUES ({legacyId},{companyId},{"S03-LEGACY"},
                    {new DateOnly(2039, 1, 1)},{new DateOnly(2039, 12, 31)},{Guid.NewGuid()})");
            var legacy = await db.Set<FiscalYear>().AsNoTracking().SingleAsync(x => x.Id == legacyId);
            Ensure(legacy.CreatedBy is null && legacy.CreatedAt is null &&
                legacy.Revision is null && legacy.LastModifiedAt is null,
                "SYS03 retroactively invented historic fiscal-year actor/time/revision");
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.FiscalYears SET [CreatedBy]={"attacker"},[Revision]={-1}
                    WHERE [CompanyId]={companyId} AND [Id]={legacyId}");
                throw new Exception("SYS03 audit columns accepted partial invalid metadata");
            }
            catch (SqlException ex) when (ex.Number == 547) { }
            await tx.RollbackAsync();
        }
        Ensure(!await db.Set<FiscalYear>().AnyAsync(x => x.Id == legacyId) &&
            old.CreatedBy is not null && old.Revision == 1,
            "SYS03 test changed an existing fiscal year or retained an injected legacy row");
        Console.WriteLine("  SQL PASS SYS03-FG04 legacy provenance unknown, invalid metadata blocked, test rollback");
    }
    private static async Task<int> Count(ErpDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
