using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class SecurityRoleAssignmentSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "030_security_role_assignments.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [SecurityRoleAssignments] ([TestOnly] int NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting role assignment migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=30").AnyAsync(),
            "Failed role assignment migration recorded version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [SecurityRoleAssignments]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=30").SingleAsync() == 1 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('SecurityRoleAssignments')").SingleAsync() == 5 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('SecurityRoleAssignments')").SingleAsync() == 3 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id=OBJECT_ID('SecurityRoleAssignments') AND is_unique=1").SingleAsync() == 2,
            "Role assignment migration fields, unique key or tenant FKs missing");
        Ensure(!await db.Set<SecurityRoleAssignment>().AnyAsync(),
            "Role assignment migration seeded user permissions");
        Console.WriteLine("  SQL PASS SYS08/SYS07 029→030 rollback/retry, tenant FKs, unique assignment, no seed");
    }

    public static async Task Guard(string connection, Guid companyId)
    {
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        db.Add(new SecurityRoleAssignment(companyId, Guid.NewGuid(), Guid.NewGuid()));
        try { await db.SaveChangesAsync(); throw new Exception("Unverified assignment persisted"); }
        catch (BusinessException ex) when (ex.Code == "security_role_assignment_policy_unavailable") { }
        await using var check = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        try
        {
            await check.Database.ExecuteSqlInterpolatedAsync($@"INSERT [SecurityRoleAssignments]
                ([CompanyId],[Id],[Version],[MembershipId],[RoleId])
                VALUES ({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()})");
            throw new Exception("Role assignment accepted missing membership/role");
        }
        catch (SqlException ex) when (ex.Number == 547) { }
        Ensure(!await check.Set<SecurityRoleAssignment>().AnyAsync(),
            "Role assignment guard or FK leaked rows");
        Console.WriteLine("  SQL PASS SYS08/SYS07 assignment write guard and missing tenant references");
    }

    private static void Ensure(bool ok, string message) { if (!ok) throw new Exception(message); }
}
