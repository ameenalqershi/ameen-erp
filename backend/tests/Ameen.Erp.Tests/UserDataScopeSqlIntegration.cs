using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Disposable SQL Server database only. No real access assignment is activated.
public static class UserDataScopeSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script=await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,"database","043_user_data_scopes_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(43,SYSUTCDATETIME())");
        await Reject("unknown migration version owner",db.Database.ExecuteSqlRawAsync(script),51002);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE 'UserDataScope%'")==0,
            "SYS08 schema version collision created tables");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.__AmeenSchemaVersions WHERE [Version]=43");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE UserDataScopeLines ([Collision] int NOT NULL)");
        await Reject("conflicting line table",db.Database.ExecuteSqlRawAsync(script),2714);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=43")==0 &&
            await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='UserDataScopes'")==0,
            "SYS08 failed schema operation committed a header/version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE UserDataScopeLines");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=43")==1 &&
            await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('UserDataScopes')")==11 &&
            await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('UserDataScopeLines')")==9 &&
            await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('UserDataScopes'),OBJECT_ID('UserDataScopeLines'))")==9,
            "SYS08 column, migration or tenant FK count mismatch");
        Ensure(await Count(db, @"SELECT COUNT(*) AS [Value] FROM (
            SELECT fk.object_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns c ON c.constraint_object_id=fk.object_id
            JOIN sys.columns local ON local.object_id=c.parent_object_id AND local.column_id=c.parent_column_id
            JOIN sys.columns remote ON remote.object_id=c.referenced_object_id AND remote.column_id=c.referenced_column_id
            WHERE fk.parent_object_id IN (OBJECT_ID('UserDataScopes'),OBJECT_ID('UserDataScopeLines'))
              AND fk.name NOT IN ('FK_UserDataScope_Company','FK_UserDataScopeLine_Company')
            GROUP BY fk.object_id
            HAVING COUNT(*)=2
              AND SUM(CASE WHEN local.name='CompanyId' AND remote.name='CompanyId' THEN 1 ELSE 0 END)=1
        ) AS [TenantCompositeFks]")==7,
            "SYS08 missing exact company-scoped two-column references");
        Ensure(!await db.Set<UserDataScope>().AnyAsync() &&
            !await db.Set<UserDataScopeLine>().AnyAsync(),
            "SYS08 migration seeded data access grants");

        var denied=new UserDataScope(Guid.NewGuid(),null,"test-only",
            Guid.NewGuid(),Guid.NewGuid(),"test",DateTimeOffset.UtcNow,
            new[] { (Guid.NewGuid(),(Guid?)null,(Guid?)null,(Guid?)null,(Guid?)null) });
        db.Add(denied);
        try { await db.SaveChangesAsync(); throw new Exception("Unapproved SYS08 EF write accepted"); }
        catch(BusinessException ex) when(ex.Code=="user_data_scope_policy_unavailable") { }
        db.ChangeTracker.Clear();
        foreach(var state in new[]{ EntityState.Modified,EntityState.Deleted })
        {
            var line=new UserDataScopeLine(Guid.NewGuid(),Guid.NewGuid(),
                Guid.NewGuid(),null,null,null,null);
            db.Entry(line).State=state;
            try { await db.SaveChangesAsync(); throw new Exception("Unverified SYS08 line mutation accepted"); }
            catch(BusinessException ex) when(ex.Code=="user_data_scope_policy_unavailable") { }
            db.ChangeTracker.Clear();
        }
        Console.WriteLine("  SQL PASS SYS08 metadata migration 043 rollback/replay/version owner/exact tenant FK and EF write guards");
    }

    public static async Task Guard(ErpDbContext db,Guid companyId,Guid branchId,string subject)
    {
        var member=await db.Set<Membership>().AsNoTracking()
            .Where(x=>x.CompanyId==companyId && x.Subject==subject)
            .Select(x=>x.Id).SingleAsync();
        // Inactive test-only role never enables any authenticated permission.
        var role=Guid.NewGuid();var now=new DateTimeOffset(2026,11,3,12,0,0,TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT SecurityRoles (CompanyId,Id,Version,BranchId,IsActive,Name,Responsibility,
                CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{role},{Guid.NewGuid()},{branchId},{false},
                {"SYS08 SQL fixture"},{"Inactive test only"},{"sql-test"},{now},{1},{now})");
        var id=Guid.NewGuid();var lineId=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT UserDataScopes (CompanyId,Id,Version,BranchId,StatusCode,
                 MembershipId,RoleId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{id},{Guid.NewGuid()},{branchId},{"test-only"},
                   {member},{role},{"sql-test"},{now},{1},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT UserDataScopeLines (CompanyId,Id,Version,ScopeId,BranchId,
                 WarehouseId,CashBoxId,AccountGroupId,CustomerGroupId)
            VALUES({companyId},{lineId},{Guid.NewGuid()},{id},{branchId},
                 NULL,NULL,NULL,NULL)");
        db.ChangeTracker.Clear();
        var data=await db.Set<UserDataScope>().AsNoTracking()
            .SingleAsync(x=>x.CompanyId==companyId && x.Id==id);
        Ensure(data.BranchId==branchId && data.StatusCode=="test-only" &&
            data.MembershipId==member && data.RoleId==role &&
            data.Revision==1 && data.CreatedBy=="sql-test" &&
            data.CreatedAt==now && data.LastModifiedAt==now &&
            data.Lines.Count==1 && data.Lines[0].CompanyId==companyId &&
            data.Lines[0].BranchId==branchId &&
            data.Lines[0].ScopeId==id &&
            data.Lines[0].WarehouseId is null && data.Lines[0].CashBoxId is null &&
            data.Lines[0].AccountGroupId is null && data.Lines[0].CustomerGroupId is null,
            "SYS08 EF/SQL source field roundtrip lost tenant or scope");

        await Reject("foreign header branch",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopes SET BranchId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={id}"),547);
        await Reject("foreign user membership",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopes SET MembershipId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={id}"),547);
        await Reject("foreign user role",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopes SET RoleId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={id}"),547);
        await Reject("foreign line branch",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopeLines SET BranchId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("foreign warehouse",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopeLines SET WarehouseId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("foreign cash box",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopeLines SET CashBoxId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("foreign parent scope",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopeLines SET ScopeId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("unverified account group",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopeLines SET AccountGroupId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("unverified customer group",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopeLines SET CustomerGroupId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={lineId}"),547);
        await Reject("unverified revision",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopes SET Revision={0} WHERE CompanyId={companyId} AND Id={id}"),547);
        await Reject("audit time regression",db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE UserDataScopes SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={companyId} AND Id={id}"),547);
        // Clean up disposable test fixture to avoid interfering with
        // independent SYS07 role tests later in SqlIntegration.Run.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE UserDataScopeLines WHERE CompanyId={companyId} AND ScopeId={id}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE UserDataScopes WHERE CompanyId={companyId} AND Id={id}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE SecurityRoles WHERE CompanyId={companyId} AND Id={role}");
        db.ChangeTracker.Clear();
        Console.WriteLine("  SQL PASS SYS08 user/role/branch/warehouse/cashbox FKs and rejected group grants; no residual access rows");
    }

    private static Task<int> Count(ErpDbContext db,string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task Reject(string label,Task work,params int[] codes)
    {
        try { await work; throw new Exception("SYS08 accepted "+label); }
        catch(SqlException ex) when(ex.Errors.Cast<SqlError>().Any(e=>codes.Contains(e.Number))) { }
    }
    private static void Ensure(bool condition,string message)
    {
        if(!condition) throw new Exception(message);
    }
}
