using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class InventoryDocumentFoundationSqlIntegration
{
    private static readonly string[] Tables =
    {
        "InventoryReceiptHeaders", "InventoryIssueHeaders", "InventoryTransferHeaders",
        "InventoryTransferReceiptHeaders", "InventoryCountHeaders",
        "InventoryAdjustmentHeaders", "InventoryDamageHeaders", "InventoryRevaluationHeaders"
    };

    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", "050_inventory_document_header_foundations.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [InventoryReceiptHeaders] ([TestOnly] int NULL)");
        await RejectSql(db.Database.ExecuteSqlRawAsync(script), 2714);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=50") == 0,
            "INV FG01 failed migration recorded version");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('InventoryIssueHeaders','InventoryTransferHeaders','InventoryTransferReceiptHeaders','InventoryCountHeaders','InventoryAdjustmentHeaders','InventoryDamageHeaders','InventoryRevaluationHeaders')") == 0,
            "INV FG01 failed migration leaked tables");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [InventoryReceiptHeaders]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=50") == 1,
            "INV FG01 migration is not idempotent");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('InventoryReceiptHeaders','InventoryIssueHeaders','InventoryTransferHeaders','InventoryTransferReceiptHeaders','InventoryCountHeaders','InventoryAdjustmentHeaders','InventoryDamageHeaders','InventoryRevaluationHeaders')") == 8,
            "INV FG01 migration did not create all eight tables");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (SELECT object_id FROM sys.tables WHERE name IN ('InventoryReceiptHeaders','InventoryIssueHeaders','InventoryTransferHeaders','InventoryTransferReceiptHeaders','InventoryCountHeaders','InventoryAdjustmentHeaders','InventoryDamageHeaders','InventoryRevaluationHeaders'))") == 23,
            "INV FG01 tenant/branch/fiscal-year/warehouse FKs missing");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('dbo.InventoryCountHeaders'),OBJECT_ID('dbo.InventoryRevaluationHeaders'))") == 4,
            "INV FG01 status/reference/date checks missing");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.InventoryReceiptHeaders')") == 5 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.InventoryCountHeaders')") == 6 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.InventoryRevaluationHeaders')") == 6,
            "INV FG01 source field columns mismatch");
        Console.WriteLine("  SQL PASS INV07/08/09/10/12/13/14/15-FG01 049->050 atomic/idempotent, 8 tables, 23 tenant FKs, no workflow");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId, Guid inventoryAccountId)
    {
        var fiscalYearId = await db.Set<FiscalYear>().Where(x => x.CompanyId == companyId).Select(x => x.Id).SingleAsync();
        var membershipId = await db.Set<Membership>().Where(x => x.CompanyId == companyId).Select(x => x.Id).FirstAsync();
        var warehouseId = Guid.NewGuid();
        // Inactive, disposable SQL-only warehouse fixture. The existing account is used only for its tenant FK.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.Warehouses(CompanyId,Id,Version,Code,Name,EnglishName,Notes,BranchId,KeeperMembershipId,InventoryAccountId,Type,TrackLocations,IsActive,Revision)
            VALUES({companyId},{warehouseId},{Guid.NewGuid()},{"INV-FG01-TEST"},{"مخزن اختبار"},{(string?)null},{(string?)null},
              {branchId},{membershipId},{inventoryAccountId},{0},{false},{false},{1})");
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        await InsertFiscal(db, "InventoryReceiptHeaders", companyId, ids[0], branchId, fiscalYearId);
        await InsertFiscal(db, "InventoryIssueHeaders", companyId, ids[1], branchId, fiscalYearId);
        await InsertFiscal(db, "InventoryTransferHeaders", companyId, ids[2], branchId, fiscalYearId);
        await InsertFiscal(db, "InventoryTransferReceiptHeaders", companyId, ids[3], branchId, fiscalYearId);
        await InsertFiscal(db, "InventoryAdjustmentHeaders", companyId, ids[4], branchId, fiscalYearId);
        await InsertFiscal(db, "InventoryDamageHeaders", companyId, ids[5], branchId, fiscalYearId);
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryCountHeaders(CompanyId,Id,Version,BranchId,StatusCode,WarehouseId)
            VALUES({companyId},{ids[6]},{Guid.NewGuid()},{(Guid?)null},{"TEST_STATUS"},{warehouseId})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryRevaluationHeaders(CompanyId,Id,Version,BranchId,StatusCode,FromDate)
            VALUES({companyId},{ids[7]},{Guid.NewGuid()},{(Guid?)null},{"TEST_STATUS"},{new DateOnly(2026,1,1)})");

        var receipt = await db.Set<InventoryReceiptHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[0]);
        var issue = await db.Set<InventoryIssueHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[1]);
        var transfer = await db.Set<InventoryTransferHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[2]);
        var transferReceipt = await db.Set<InventoryTransferReceiptHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[3]);
        var adjustment = await db.Set<InventoryAdjustmentHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[4]);
        var damage = await db.Set<InventoryDamageHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[5]);
        var count = await db.Set<InventoryCountHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[6]);
        var revaluation = await db.Set<InventoryRevaluationHeader>().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == ids[7]);
        Ensure(new CompanyEntity[] { receipt, issue, transfer, transferReceipt, adjustment, damage }.All(x =>
            x.CompanyId == companyId && x.Id != Guid.Empty && x.Version != Guid.Empty), "INV fiscal scope SQL/EF roundtrip mismatch");
        Ensure(receipt.BranchId == branchId && receipt.FiscalYearId == fiscalYearId &&
            issue.BranchId == branchId && issue.FiscalYearId == fiscalYearId &&
            transfer.BranchId == branchId && transfer.FiscalYearId == fiscalYearId &&
            transferReceipt.BranchId == branchId && transferReceipt.FiscalYearId == fiscalYearId &&
            adjustment.BranchId == branchId && adjustment.FiscalYearId == fiscalYearId &&
            damage.BranchId == branchId && damage.FiscalYearId == fiscalYearId,
            "INV fiscal scope SQL/EF values mismatch");
        Ensure(count.BranchId is null && count.StatusCode == "TEST_STATUS" && count.WarehouseId == warehouseId &&
            revaluation.BranchId is null && revaluation.StatusCode == "TEST_STATUS" &&
            revaluation.FromDate == new DateOnly(2026,1,1), "INV12/15 FG01 SQL/EF roundtrip mismatch");

        var probes = new CompanyEntity[]
        {
            new InventoryReceiptHeader(companyId, branchId, fiscalYearId),
            new InventoryIssueHeader(companyId, branchId, fiscalYearId),
            new InventoryTransferHeader(companyId, branchId, fiscalYearId),
            new InventoryTransferReceiptHeader(companyId, branchId, fiscalYearId),
            new InventoryAdjustmentHeader(companyId, branchId, fiscalYearId),
            new InventoryDamageHeader(companyId, branchId, fiscalYearId),
            new InventoryCountHeader(companyId, null, "TEST_STATUS", warehouseId),
            new InventoryRevaluationHeader(companyId, null, "TEST_STATUS", new DateOnly(2026,1,1))
        };
        foreach (var probe in probes)
        {
            foreach (var state in new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted })
            {
                db.ChangeTracker.Clear();
                db.Entry(probe).State = state;
                try { await db.SaveChangesAsync(); throw new Exception("INV FG01 EF " + state + " escaped policy guard"); }
                catch (BusinessException e) when (e.Code == "inventory_document_foundation_policy_unavailable") { }
            }
        }
        db.ChangeTracker.Clear();
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryReceiptHeaders(CompanyId,Id,Version,BranchId,FiscalYearId)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{fiscalYearId})"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryReceiptHeaders(CompanyId,Id,Version,BranchId,FiscalYearId)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{branchId},{Guid.NewGuid()})"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryCountHeaders(CompanyId,Id,Version,BranchId,StatusCode,WarehouseId)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{(Guid?)null},{" "},{warehouseId})"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryCountHeaders(CompanyId,Id,Version,BranchId,StatusCode,WarehouseId)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{(Guid?)null},{"TEST_STATUS"},{Guid.NewGuid()})"), 547);
        await RejectSql(db.Database.ExecuteSqlInterpolatedAsync($@"INSERT dbo.InventoryRevaluationHeaders(CompanyId,Id,Version,BranchId,StatusCode,FromDate)
            VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{(Guid?)null},{"TEST_STATUS"},{DateOnly.MinValue})"), 547);

        var audits = await db.Set<AuditEvent>().CountAsync();
        var outbox = await db.Set<OutboxEvent>().CountAsync();
        var journals = await db.Set<JournalEntry>().CountAsync();
        Ensure(await db.Set<InventoryReceiptHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryIssueHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryTransferHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryTransferReceiptHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryAdjustmentHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryDamageHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryCountHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<InventoryRevaluationHeader>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<AuditEvent>().CountAsync() == audits && await db.Set<OutboxEvent>().CountAsync() == outbox &&
            await db.Set<JournalEntry>().CountAsync() == journals,
            "INV FG01 fail-closed EF write changed docs or audit/outbox/ledger");
        foreach (var table in Tables)
            await DeleteTenantRows(db, table, companyId);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.Warehouses WHERE CompanyId={companyId} AND Id={warehouseId}");
        Console.WriteLine("  SQL PASS INV FG01 SQL/EF roundtrip, tenant refs, invalid branch/year/warehouse/status/date and EF deny");
    }

    private static Task<int> DeleteTenantRows(ErpDbContext db, string table, Guid companyId) => table switch
    {
        "InventoryReceiptHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryReceiptHeaders WHERE CompanyId={companyId}"),
        "InventoryIssueHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryIssueHeaders WHERE CompanyId={companyId}"),
        "InventoryTransferHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryTransferHeaders WHERE CompanyId={companyId}"),
        "InventoryTransferReceiptHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryTransferReceiptHeaders WHERE CompanyId={companyId}"),
        "InventoryCountHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryCountHeaders WHERE CompanyId={companyId}"),
        "InventoryAdjustmentHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryAdjustmentHeaders WHERE CompanyId={companyId}"),
        "InventoryDamageHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryDamageHeaders WHERE CompanyId={companyId}"),
        "InventoryRevaluationHeaders" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.InventoryRevaluationHeaders WHERE CompanyId={companyId}"),
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    };

    private static Task InsertFiscal(ErpDbContext db, string table, Guid companyId, Guid id, Guid branchId, Guid fiscalYearId)
    {
        var sql = table switch
        {
            "InventoryReceiptHeaders" => "INSERT dbo.InventoryReceiptHeaders(CompanyId,Id,Version,BranchId,FiscalYearId) VALUES(@c,@i,@v,@b,@f)",
            "InventoryIssueHeaders" => "INSERT dbo.InventoryIssueHeaders(CompanyId,Id,Version,BranchId,FiscalYearId) VALUES(@c,@i,@v,@b,@f)",
            "InventoryTransferHeaders" => "INSERT dbo.InventoryTransferHeaders(CompanyId,Id,Version,BranchId,FiscalYearId) VALUES(@c,@i,@v,@b,@f)",
            "InventoryTransferReceiptHeaders" => "INSERT dbo.InventoryTransferReceiptHeaders(CompanyId,Id,Version,BranchId,FiscalYearId) VALUES(@c,@i,@v,@b,@f)",
            "InventoryAdjustmentHeaders" => "INSERT dbo.InventoryAdjustmentHeaders(CompanyId,Id,Version,BranchId,FiscalYearId) VALUES(@c,@i,@v,@b,@f)",
            "InventoryDamageHeaders" => "INSERT dbo.InventoryDamageHeaders(CompanyId,Id,Version,BranchId,FiscalYearId) VALUES(@c,@i,@v,@b,@f)",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        return db.Database.ExecuteSqlRawAsync(sql,
            new SqlParameter("@c", companyId), new SqlParameter("@i", id),
            new SqlParameter("@v", Guid.NewGuid()), new SqlParameter("@b", branchId),
            new SqlParameter("@f", fiscalYearId));
    }

    private static Task<int> Count(ErpDbContext db, string sql) => db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task RejectSql(Task operation, int number)
    {
        try { await operation; throw new Exception("INV FG01 SQL rejection unexpectedly succeeded: " + number); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == number)) { }
    }
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
