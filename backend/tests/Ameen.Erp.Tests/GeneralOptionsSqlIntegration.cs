using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Runs against SqlIntegration's newly created disposable database only.
public static class GeneralOptionsSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "041_general_options_foundation.sql"));
        // Replay with a conflicting version must never claim someone else's migration.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(41,SYSUTCDATETIME())");
        await Reject("version collision", db.Database.ExecuteSqlRawAsync(script), 51002);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE 'GeneralOptions%'") == 0,
            "SYS11 version collision created tables");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM dbo.__AmeenSchemaVersions WHERE [Version]=41");
        // Test an actual DDL failure and transaction rollback.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE GeneralOptionsAccountLines ([Collision] int NOT NULL)");
        await Reject("table collision", db.Database.ExecuteSqlRawAsync(script), 2714);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=41") == 0 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='GeneralOptionsPolicies'") == 0,
            "SYS11 failed migration leaked tables or version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE GeneralOptionsAccountLines");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script); // Idempotent replay
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=41") == 1,
            "SYS11 version not idempotent");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('GeneralOptionsPolicies')") == 13 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('GeneralOptionsAccountLines')") == 8 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('GeneralOptionsPolicies'),OBJECT_ID('GeneralOptionsAccountLines'))") == 5,
            "SYS11 original fields or tenant FK missing");
        Ensure(!await db.Set<GeneralOptionsPolicy>().AnyAsync() &&
            !await db.Set<GeneralOptionsAccountLine>().AnyAsync(),
            "SYS11 migration seeded a financial policy");

        var denied = new GeneralOptionsPolicy(Guid.NewGuid(), null, "test-only",
            "unapproved-valuation", false, "unapproved-delivery", 0, "sql-test",
            DateTimeOffset.UtcNow, Array.Empty<(string,string,Guid,string)>());
        db.Add(denied);
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("SYS11 EF accepted unapproved policy");
        }
        catch (BusinessException ex) when (ex.Code=="general_options_policy_unavailable") { }
        db.Entry(denied).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS SYS11-01 schema 041 atomic rollback, ownership, replay and denied EF writes");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId, Guid accountId)
    {
        var id=Guid.NewGuid(); var now=new DateTimeOffset(2026,11,3,12,0,0,TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT GeneralOptionsPolicies
             (CompanyId,Id,Version,BranchId,StatusCode,InventoryValuationCode,
              BlockNegativeStock,SalesDeliveryModeCode,QuantityPrecision,CreatedBy,
              CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{id},{Guid.NewGuid()},{branchId},{"test-only"},
             {"unapproved-valuation"},{true},{"unapproved-delivery"},{4},
             {"sql-test"},{now},{1},{now})");
        var lineId=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT GeneralOptionsAccountLines
             (CompanyId,Id,Version,PolicyId,EventCode,AccountRoleCode,AccountId,Scope)
            VALUES({companyId},{lineId},{Guid.NewGuid()},{id},
             {"test-event"},{"test-role"},{accountId},{"test-scope"})");
        db.ChangeTracker.Clear();
        var model=await db.Set<GeneralOptionsPolicy>().AsNoTracking().Include(x=>x.Lines)
            .SingleAsync(x=>x.CompanyId==companyId && x.Id==id);
        Ensure(model.BranchId==branchId && model.StatusCode=="test-only" &&
            model.InventoryValuationCode=="unapproved-valuation" &&
            model.SalesDeliveryModeCode=="unapproved-delivery" &&
            model.QuantityPrecision==4 && model.BlockNegativeStock &&
            model.CreatedBy=="sql-test" && model.Revision==1 &&
            model.CreatedAt==now && model.LastModifiedAt==now &&
            model.Lines.Count==1 && model.Lines[0].AccountId==accountId &&
            model.Lines[0].Scope=="test-scope",
            "SYS11 persistence roundtrip mismatch");
        await Reject("unknown branch", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE GeneralOptionsPolicies SET BranchId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={id}"),547);
        await Reject("unknown account", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE GeneralOptionsAccountLines SET AccountId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("foreign policy", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE GeneralOptionsAccountLines SET PolicyId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("invalid precision", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE GeneralOptionsPolicies SET QuantityPrecision={-1} WHERE CompanyId={companyId} AND Id={id}"),547);
        await Reject("audit time regression", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE GeneralOptionsPolicies SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={companyId} AND Id={id}"),547);
        Console.WriteLine("  SQL PASS SYS11-01 tenant FK, precision, date checks and EF field roundtrip");
    }

    private static Task<int> Count(ErpDbContext db, string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    private static async Task Reject(string label, Task work, params int[] errorCodes)
    {
        try { await work; throw new Exception("SYS11 accepted " + label); }
        catch (SqlException ex) when(ex.Errors.Cast<SqlError>().Any(e => errorCodes.Contains(e.Number))) { }
    }
    private static void Ensure(bool ok, string error)
    {
        if (!ok) throw new Exception(error);
    }
}
