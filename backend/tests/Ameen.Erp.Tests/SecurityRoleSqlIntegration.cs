using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class SecurityRoleSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "029_security_role_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [SecurityRoles] ([TestOnly] int NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting SYS07 migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=29").AnyAsync(),
            "Failed SYS07 migration recorded version");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='SecurityRoleRules'").SingleAsync() == 0 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name='FK_ApprovalRouteStage_ApproverRole'").SingleAsync() == 0,
            "Failed SYS07 migration leaked rule table or SYS09 FK");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [SecurityRoles]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=29").SingleAsync() == 1,
            "SYS07 migration not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('SecurityRoles')").SingleAsync() == 11 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('SecurityRoleRules')").SingleAsync() == 9,
            "SYS07 schema fields mismatch");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('SecurityRoles')").SingleAsync() == 2 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('SecurityRoleRules')").SingleAsync() == 3 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name='FK_ApprovalRouteStage_ApproverRole'").SingleAsync() == 1,
            "SYS07/SYS09 tenant FKs missing");
        Ensure(!await db.Set<SecurityRole>().AnyAsync() && !await db.Set<SecurityRoleRule>().AnyAsync(),
            "SYS07 migration invented roles or permissions");
        Console.WriteLine("  SQL PASS SYS07 028→029 atomic/idempotent, tenant FKs, SYS09 role FK, no seeded grants");
    }

    public static async Task Guard(string connection, Guid companyId)
    {
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        var role = new SecurityRole(companyId, null, "Role", "No runtime grant",
            "sql-test", DateTimeOffset.UtcNow);
        role.AddRule("GL03", "POST", true, null, null, DateTimeOffset.UtcNow.AddSeconds(1));
        db.Add(role);
        try { await db.SaveChangesAsync(); throw new Exception("SYS07 accepted unverified grant"); }
        catch (BusinessException ex) when (ex.Code == "security_role_policy_unavailable") { }
        await using var check = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        Ensure(!await check.Set<SecurityRole>().AnyAsync() && !await check.Set<SecurityRoleRule>().AnyAsync(),
            "SYS07 EF guard leaked grants to SQL");
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        try
        {
            await check.Database.ExecuteSqlInterpolatedAsync($@"INSERT [SecurityRoles]
                ([CompanyId],[Id],[Version],[BranchId],[IsActive],[Name],[Responsibility],
                 [CreatedBy],[CreatedAt],[Revision],[LastModifiedAt])
                VALUES ({companyId},{id},{Guid.NewGuid()},{Guid.NewGuid()},1,
                        {"Orphan role"},{"Responsibility"},{"test"},{now},1,{now})");
            throw new Exception("SYS07 accepted foreign branch");
        }
        catch (SqlException ex) when (ex.Number == 547) { }
        try
        {
            await check.Database.ExecuteSqlInterpolatedAsync($@"INSERT [SecurityRoleRules]
                ([CompanyId],[Id],[Version],[RoleId],[ScreenCode],[OperationCode],[Allowed],[AmountLimit],[CurrencyId])
                VALUES ({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},
                        {"GL03"},{"POST"},1,NULL,NULL)");
            throw new Exception("SYS07 accepted missing tenant role");
        }
        catch (SqlException ex) when (ex.Number == 547) { }
        Ensure(!await check.Set<SecurityRole>().AnyAsync() && !await check.Set<SecurityRoleRule>().AnyAsync(),
            "Failed SYS07 tenant FK test left SQL rows");
        Console.WriteLine("  SQL PASS SYS07 guarded writes and rejected branch/role tenant FKs");
    }

    private static void Ensure(bool ok, string message) { if (!ok) throw new Exception(message); }
}
