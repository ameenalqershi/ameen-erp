using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Isolated SQL 039 acceptance, no live advance settlement or financial posting.
public static class CashAdvanceSqlIntegration
{
    private const string FileName = "039_cash_advances_foundation.sql";

    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", FileName));
        // The header DDL must rollback if a later dependent table fails.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE [CashAdvanceLines] ([Conflict] int NOT NULL)");
        await RejectSql("atomic upgrade conflict", 2714,
            db.Database.ExecuteSqlRawAsync(script));
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=39").AnyAsync(),
            "CASH06 failed migration recorded receipt");
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT 1 AS [Value] FROM sys.tables WHERE name=N'CashAdvances'").AnyAsync(),
            "CASH06 migration leaked parent DDL");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [CashAdvanceLines]");

        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=39").SingleAsync() == 1,
            "CASH06 upgrade not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CashAdvances')").SingleAsync() == 21 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CashAdvanceLines')").SingleAsync() == 7,
            "CASH06 original header/line or technical columns not preserved");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.parent_object_id IN (OBJECT_ID(N'dbo.CashAdvances'),
                                         OBJECT_ID(N'dbo.CashAdvanceLines'))
              AND fk.name NOT IN (N'FK_CashAdvance_Company', N'FK_CashAdvanceLine_Company')
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id=fk.object_id)=2
              AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                  JOIN sys.columns col ON col.object_id=fc.parent_object_id
                                        AND col.column_id=fc.parent_column_id
                  WHERE fc.constraint_object_id=fk.object_id AND col.name=N'CompanyId')
          ").SingleAsync() == 4, "CASH06 tenant-scoped FKs incomplete");

        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var invalid = new CashAdvance(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "0001", new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3),
            "SAR", 1m, "test", null, "Pending", Guid.NewGuid(), "Unapproved",
            20m, new DateOnly(2026, 11, 5), "test", now);
        db.Add(invalid);
        await RejectDomain("cash_advance_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(invalid).State = EntityState.Detached;
        var line = new CashAdvanceLine(Guid.NewGuid(), Guid.NewGuid(), null, 10m, null);
        db.Add(line);
        await RejectDomain("cash_advance_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(line).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH06-01 atomic/idempotent schema 039 and tenant FKs; EF write fail-closed");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId)
    {
        var yearId = await db.Set<FiscalYear>().Where(x => x.CompanyId == companyId)
            .Select(x => x.Id).FirstAsync();
        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var advanceId = Guid.NewGuid();
        var holderId = Guid.NewGuid(); // HR03 source is not implemented.
        var documentDate = new DateOnly(2026, 11, 3);
        // For raw SQL bind the financial scale explicitly: the provider's
        // inferred decimal(18,2) would round these approved source values.
        var rate = new SqlParameter("@advanceRate", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 10, Value = 1.1234567890m };
        var amount = new SqlParameter("@advanceAmount", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 123.000001m };
        var settlement = new SqlParameter("@advanceSettlement", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 1.000001m };
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [CashAdvances] ([CompanyId],[Id],[Version],[BranchId],
               [FiscalYearId],[Number],[DocumentDate],[AccountingDate],[CurrencyCode],
               [ExchangeRate],[Description],[ExternalReference],[State],[HolderId],
               [AdvanceType],[Amount],[ExpectedSettlementDate],[CreatedBy],
               [CreatedAt],[Revision],[LastModifiedAt])
            VALUES ({companyId},{advanceId},{Guid.NewGuid()},{branchId},
               {yearId},{"000009"},{documentDate},{documentDate},{"SAR"},
               {rate},{"purpose"},{"EXT09"},{"Pending"},{holderId},
               {"Unapproved"},{amount},{documentDate.AddDays(14)},{"tester"},
               {now},{1},{now})");
        var saved = await db.Set<CashAdvance>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == advanceId);
        Ensure(saved.Number == "000009" && saved.Amount == 123.000001m &&
            saved.ExchangeRate == 1.1234567890m &&
            saved.HolderId == holderId && saved.ExternalReference == "EXT09" &&
            saved.ExpectedSettlementDate == documentDate.AddDays(14) &&
            saved.Revision == 1 && saved.CreatedAt == now,
            "CASH06 header precision/audit round-trip failed");

        var lineId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [CashAdvanceLines] ([CompanyId],[Id],[Version],
                [AdvanceId],[ExpenseReferenceId],[SettlementAmount],[AttachmentReference])
            VALUES ({companyId},{lineId},{Guid.NewGuid()},{advanceId},
                {Guid.NewGuid()},{settlement},{"scan-001"})");
        var savedLine = await db.Set<CashAdvanceLine>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == lineId);
        Ensure(savedLine.SettlementAmount == 1.000001m &&
            savedLine.AttachmentReference == "scan-001", "CASH06 settlement line precision lost");

        await RejectSql("unknown tenant branch", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvances] SET [BranchId]={Guid.NewGuid()} WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));
        await RejectSql("unknown fiscal year", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvances] SET [FiscalYearId]={Guid.NewGuid()} WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));
        await RejectSql("negative amount", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvances] SET [Amount]={-1m} WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));
        await RejectSql("zero rate", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvances] SET [ExchangeRate]={0m} WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));
        await RejectSql("invalid revision", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvances] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));
        await RejectSql("audit rollback", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvances] SET [LastModifiedAt]={now.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));
        await RejectSql("line overdraw not allowed as negative entry", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvanceLines] SET [SettlementAmount]={-1m} WHERE [CompanyId]={companyId} AND [Id]={lineId}"));
        await RejectSql("unknown advance owner", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashAdvanceLines] SET [AdvanceId]={Guid.NewGuid()} WHERE [CompanyId]={companyId} AND [Id]={lineId}"));
        await RejectSql("parent with settlement lines", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [CashAdvances] WHERE [CompanyId]={companyId} AND [Id]={advanceId}"));

        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, saved, "cash_advance_policy_unavailable");
        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, savedLine, "cash_advance_policy_unavailable");

        var unchanged = await db.Set<CashAdvance>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == advanceId);
        Ensure(unchanged.Amount == 123.000001m && unchanged.Revision == 1 &&
            (await db.Set<CashAdvanceLine>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == companyId && x.Id == lineId)).SettlementAmount == 1.000001m,
            "CASH06 rejected mutation leaked changes");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [CashAdvanceLines] WHERE [CompanyId]={companyId} AND [AdvanceId]={advanceId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [CashAdvances] WHERE [CompanyId]={companyId} AND [Id]={advanceId}");
        Ensure(!await db.Set<CashAdvance>().AnyAsync() &&
            !await db.Set<CashAdvanceLine>().AnyAsync(), "CASH06 fixture not cleaned");
        Console.WriteLine("  SQL PASS CASH06-01 exact decimal, tenant FKs, atomic rejection, fixture cleanup");
    }

    private static async Task RejectSql(string scenario, int code, Task<int> op)
    {
        try { await op; }
        catch (SqlException ex) when (ex.Number == code ||
            ex.Errors.Cast<SqlError>().Any(x => x.Number == code)) { return; }
        throw new Exception("CASH06 SQL should reject: " + scenario);
    }

    private static async Task RejectDomain(string code, Func<Task<int>> op)
    {
        try { await op(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH06 EF should reject: " + code);
    }

    private static void Ensure(bool ok, string reason)
    {
        if (!ok) throw new Exception(reason);
    }
}
