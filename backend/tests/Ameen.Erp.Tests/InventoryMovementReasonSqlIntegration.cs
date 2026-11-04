using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Runs only on the automatically created, disposable SQL integration database.
public static class InventoryMovementReasonSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "049_inventory_movement_reason_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE [InventoryMovementReasons] ([Conflict] int NOT NULL)");
        await RejectSql(db.Database.ExecuteSqlRawAsync(script), 2714);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49") == 0,
            "INV16 failed migration recorded version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [InventoryMovementReasons]");

        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(49,SYSUTCDATETIME())");
        await RejectSql(db.Database.ExecuteSqlRawAsync(script), 51002);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.__AmeenSchemaVersions WHERE [Version]=49");

        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49") == 1,
            "INV16 migration is not idempotent");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.InventoryMovementReasons')") == 15,
            "INV16 expected source fields and optimistic Version");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.InventoryMovementReasons')") == 4,
            "INV16 company/branch/account/approval tenant FKs missing");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.InventoryMovementReasons')") == 3,
            "INV16 audit/shape/reference checks missing");
        Ensure(await db.Set<InventoryMovementReason>().CountAsync() == 0,
            "INV16 unexpectedly seeded active movement reasons");
        Console.WriteLine("  SQL PASS INV16 048->049 atomic/replayed structure, 15 fields, 4 scoped FKs, 0 seeds");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId,
        Guid accountId, string actor)
    {
        var model = db.Model.FindEntityType(typeof(InventoryMovementReason))
            ?? throw new Exception("INV16 EF model missing");
        Ensure(model.FindPrimaryKey()!.Properties.Select(x => x.Name).SequenceEqual(
                new[] { nameof(InventoryMovementReason.CompanyId), nameof(InventoryMovementReason.Id) }),
            "INV16 EF tenant primary key mismatch");
        Ensure(model.FindProperty(nameof(InventoryMovementReason.Version))!.IsConcurrencyToken,
            "INV16 EF version is not a concurrency token");
        Ensure(model.GetForeignKeys().Count(x => x.Properties.Count == 2 &&
            x.Properties[0].Name == nameof(InventoryMovementReason.CompanyId)) == 3,
            "INV16 EF scoped references missing");

        var now = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        var routeId = Guid.NewGuid(); // No approved route is seeded.
        var record = new InventoryMovementReason(companyId, branchId, "TEST_STATUS",
            "TEST", "وصف", "TEST_DIRECTION", accountId, true, routeId, actor, now);

        // Even direct SQL cannot bypass a missing tenant-specific approval route.
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.InventoryMovementReasons(
              CompanyId,Id,Version,BranchId,StatusCode,ReasonCode,Description,
              DirectionCode,CounterAccountId,RequireCostCenter,ApprovalRouteId,
              CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{branchId},
              {"TEST_STATUS"},{"TEST"},{"وصف"},{"TEST_DIRECTION"},{accountId},{true},
              {routeId},{actor},{now},{1},{now})"), 547);

        // A synthetic *inactive* route is used only in this disposable SQL
        // database to verify composite tenant FKs and exact EF field mapping.
        // No approval policy is activated or seeded in production.
        var currencyId = await db.Set<Currency>().Where(x =>
            x.CompanyId == companyId && x.Code == "SAR").Select(x => x.Id).SingleAsync();
        var fixtureRouteId = Guid.NewGuid();
        var reasonId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.ApprovalRoutes(
              CompanyId,Id,Version,BranchId,IsActive,DocumentType,
              MinimumAmount,MaximumAmount,CurrencyId,SeparateCreatorAndApprover,
              CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{fixtureRouteId},{Guid.NewGuid()},{branchId},{false},
                {"TEST_INV16"},{0m},{(decimal?)null},{currencyId},{true},
                {actor},{now},{1},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.InventoryMovementReasons(
              CompanyId,Id,Version,BranchId,StatusCode,ReasonCode,Description,
              DirectionCode,CounterAccountId,RequireCostCenter,ApprovalRouteId,
              CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{reasonId},{Guid.NewGuid()},{branchId},
              {"TEST_STATUS"},{"TEST"},{"وصف اختباري"},{"TEST_DIRECTION"},{accountId},
              {true},{fixtureRouteId},{actor},{now},{1},{now})");
        var saved = await db.Set<InventoryMovementReason>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == reasonId);
        Ensure(saved.CompanyId == companyId && saved.BranchId == branchId &&
            saved.StatusCode == "TEST_STATUS" && saved.ReasonCode == "TEST" &&
            saved.Description == "وصف اختباري" && saved.DirectionCode == "TEST_DIRECTION" &&
            saved.CounterAccountId == accountId && saved.RequireCostCenter &&
            saved.ApprovalRouteId == fixtureRouteId && saved.CreatedBy == actor &&
            saved.CreatedAt == now && saved.LastModifiedAt == now && saved.Revision == 1,
            "INV16 SQL/EF field roundtrip mismatch");
        Ensure(await db.Set<InventoryMovementReason>().AsNoTracking()
            .CountAsync(x => x.CompanyId == Guid.NewGuid()) == 0,
            "INV16 query disclosed other tenant");
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.InventoryMovementReasons SET BranchId={Guid.NewGuid()}
            WHERE CompanyId={companyId} AND Id={reasonId}"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.InventoryMovementReasons SET CounterAccountId={Guid.NewGuid()}
            WHERE CompanyId={companyId} AND Id={reasonId}"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.InventoryMovementReasons SET ApprovalRouteId={Guid.NewGuid()}
            WHERE CompanyId={companyId} AND Id={reasonId}"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.InventoryMovementReasons SET Revision={0}
            WHERE CompanyId={companyId} AND Id={reasonId}"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.InventoryMovementReasons SET LastModifiedAt={now.AddDays(-1)}
            WHERE CompanyId={companyId} AND Id={reasonId}"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.InventoryMovementReasons SET Description={" "}
            WHERE CompanyId={companyId} AND Id={reasonId}"), 547);

        var originalAudits = await db.Set<AuditEvent>().CountAsync();
        var originalOutbox = await db.Set<OutboxEvent>().CountAsync();
        var originalJournals = await db.Set<JournalEntry>().CountAsync();
        foreach (var state in new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted })
        {
            db.ChangeTracker.Clear();
            db.Entry(record).State = state;
            try
            {
                await db.SaveChangesAsync();
                throw new Exception("INV16 EF " + state + " escaped policy guard");
            }
            catch (BusinessException e) when (e.Code == "inventory_movement_reason_policy_unavailable") { }
        }
        db.ChangeTracker.Clear();
        // Remove test-only rows; the configured API/EF workflow remains closed.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE dbo.InventoryMovementReasons
            WHERE CompanyId={companyId} AND Id={reasonId}");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE dbo.ApprovalRoutes
            WHERE CompanyId={companyId} AND Id={fixtureRouteId}");
        Ensure(await db.Set<InventoryMovementReason>().CountAsync() == 0 &&
            await db.Set<AuditEvent>().CountAsync() == originalAudits &&
            await db.Set<OutboxEvent>().CountAsync() == originalOutbox &&
            await db.Set<JournalEntry>().CountAsync() == originalJournals,
            "INV16 failed mutation wrote policy or financial side effects");
        Console.WriteLine("  SQL PASS INV16 source FK and audit checks, EF roundtrip, no active route or audit/ledger effects");
    }

    private static Task<int> Count(ErpDbContext db, string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task RejectSql(Task operation, int number)
    {
        try
        {
            await operation;
            throw new Exception("INV16 SQL rejected operation unexpectedly succeeded: " + number);
        }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == number)) { }
    }
    private static void Ensure(bool check, string message)
    {
        if (!check) throw new Exception(message);
    }
}
