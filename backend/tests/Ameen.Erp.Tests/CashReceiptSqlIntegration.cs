using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// CASH03-01 test database is disposable. Never promote these raw SQL fixtures
// to an application write path; the actual receipt EF workflow remains denied.
public static class CashReceiptSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "034_cash_receipts_foundation.sql"));

        // Failure after creating header must rollback all DDL and version 34.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [CashReceiptLines] ([Conflict] int NOT NULL)");
        await RejectSql("conflicting line table aborts migration", 2714,
            db.Database.ExecuteSqlRawAsync(script));
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=34").AnyAsync(),
            "CASH03 failed migration recorded schema version");
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT 1 AS [Value] FROM sys.tables WHERE name=N'CashReceipts'").AnyAsync(),
            "CASH03 failed migration left partial receipt table");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [CashReceiptLines]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);

        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=34").SingleAsync() == 1,
            "CASH03 upgrade not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CashReceipts')").SingleAsync() == 23,
            "CASH03 receipt header fields not fully persisted");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CashReceiptLines')").SingleAsync() == 7,
            "CASH03 allocation line fields not fully persisted");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CashReceipts')").SingleAsync() == 6,
            "CASH03 expected tenant FKs missing");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CashReceiptLines')").SingleAsync() == 2,
            "CASH03 line company/receipt FKs missing");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.parent_object_id IN (OBJECT_ID(N'dbo.CashReceipts'),
                                          OBJECT_ID(N'dbo.CashReceiptLines'))
              AND fk.name NOT IN (N'FK_CashReceipt_Company',N'FK_CashReceiptLine_Company')
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id=fk.object_id)=2
              AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                  JOIN sys.columns col ON col.object_id=fc.parent_object_id
                                        AND col.column_id=fc.parent_column_id
                  WHERE fc.constraint_object_id=fk.object_id AND col.name=N'CompanyId')
            ").SingleAsync() == 6, "CASH03 references must all be company-scoped");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CashReceipts')").SingleAsync() == 5 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CashReceiptLines')").SingleAsync() == 1,
            "CASH03 revision/audit/amount/rate checks missing");

        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var probe = new CashReceipt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "0001", new(2026,11,1), new(2026,11,2), "SAR", 1m, "test",
            null, "Unreviewed", Guid.NewGuid(), "Unclassified",
            null, null, 1m, null, "test", now);
        db.Add(probe);
        await RejectDomain("cash_receipt_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(probe).State = EntityState.Detached;
        var lineProbe = new CashReceiptLine(Guid.NewGuid(), Guid.NewGuid(), null, 0m, 0m);
        db.Add(lineProbe);
        await RejectDomain("cash_receipt_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(lineProbe).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH03-01 atomic/idempotent 034, composite FKs, EF fail-closed");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId, Guid cashBoxId)
    {
        var yearId = await db.Set<FiscalYear>().Where(x => x.CompanyId == companyId)
            .Select(x => x.Id).FirstAsync();
        var now = new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero);
        var payerId = Guid.NewGuid(); // SAL11 party table not present; no fake FK.
        var receiptId = Guid.NewGuid();
        var box = await db.Set<CashBox>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == cashBoxId);
        var auditCount = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId);
        var outboxCount = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == companyId);
        var journalCount = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == companyId);

        // Raw interpolated SQL does not know the target column's custom
        // precision. Bind decimal(20,10)/(20,6) explicitly so that SQL input
        // is not rounded by the provider's default decimal(18,2) mapping.
        var rate = new SqlParameter("@rate", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 10, Value = 1.1234567890m };
        var amount = new SqlParameter("@receiptAmount", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 120.000001m };
        var allocated = new SqlParameter("@allocated", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 30.000001m };
        var discount = new SqlParameter("@discount", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 0.000001m };
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [CashReceipts] ([CompanyId],[Id],[Version],[BranchId],[FiscalYearId],
                [Number],[DocumentDate],[AccountingDate],[CurrencyCode],[ExchangeRate],
                [Description],[ExternalReference],[State],[PayerId],[PaymentMethod],
                [CashBoxId],[BankAccountId],[Amount],[ChequeReference],[CreatedBy],[CreatedAt],
                [Revision],[LastModifiedAt])
            VALUES ({companyId},{receiptId},{Guid.NewGuid()},{branchId},{yearId},
                {"00000123"},{new DateTime(2026,11,1)},{new DateTime(2026,11,2)},
                {"SAR"},{rate},{"SQL structural test"},{"0000005"},{"Unreviewed"},
                {payerId},{"Unclassified"},{cashBoxId},{(Guid?)null},{amount},
                {"000000099"},{"structural-test"},{now},{1},{now})");
        var receipt = await db.Set<CashReceipt>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == receiptId);
        Ensure(receipt.Number == "00000123" && receipt.ExternalReference == "0000005" &&
            receipt.ChequeReference == "000000099" && receipt.Amount == 120.000001m &&
            receipt.ExchangeRate == 1.1234567890m &&
            receipt.CurrencyCode == "SAR" && receipt.CashBoxId == cashBoxId &&
            receipt.BankAccountId is null && receipt.PayerId == payerId &&
            receipt.BranchId == branchId && receipt.FiscalYearId == yearId &&
            receipt.CreatedAt == now && receipt.LastModifiedAt == now &&
            receipt.Revision == 1 && receipt.PaymentMethod == "Unclassified",
            $"CASH03 SQL roundtrip mismatched: number={receipt.Number}; external={receipt.ExternalReference}; " +
            $"cheque={receipt.ChequeReference}; amount={receipt.Amount}; rate={receipt.ExchangeRate}; " +
            $"currency={receipt.CurrencyCode}; boxMatch={receipt.CashBoxId == cashBoxId}; " +
            $"bankNull={receipt.BankAccountId is null}; payerMatch={receipt.PayerId == payerId}; " +
            $"branchMatch={receipt.BranchId == branchId}; yearMatch={receipt.FiscalYearId == yearId}; " +
            $"createdMatch={receipt.CreatedAt == now}; modifiedMatch={receipt.LastModifiedAt == now}; " +
            $"revision={receipt.Revision}; method={receipt.PaymentMethod}");

        var allocationId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [CashReceiptLines] ([CompanyId],[Id],[Version],[ReceiptId],
                [InvoiceId],[AllocatedAmount],[SettlementDiscount])
            VALUES ({companyId},{allocationId},{Guid.NewGuid()},{receiptId},
                {(Guid?)null},{allocated},{discount})");
        var allocation = await db.Set<CashReceiptLine>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == allocationId);
        Ensure(allocation.InvoiceId is null && allocation.AllocatedAmount == 30.000001m &&
            allocation.SettlementDiscount == 0.000001m && allocation.ReceiptId == receiptId,
            "CASH03 original conditional line fields SQL roundtrip failed");

        var unknown = Guid.NewGuid();
        await RejectSql("invalid branch tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [BranchId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("invalid fiscal year tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [FiscalYearId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("invalid currency tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [CurrencyCode]={"ZZZ"} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("invalid cashbox tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [CashBoxId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("invalid bank tenant FK", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [BankAccountId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("nonpositive receipt amount", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [Amount]={0m} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("nonpositive exchange rate", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [ExchangeRate]={0m} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("zero revision", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("regressing audit time", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [LastModifiedAt]={now.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("blank method", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceipts] SET [PaymentMethod]={"   "} WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));
        await RejectSql("negative allocated amount", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceiptLines] SET [AllocatedAmount]={-1m} WHERE [CompanyId]={companyId} AND [Id]={allocationId}"));
        await RejectSql("negative discount", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceiptLines] SET [SettlementDiscount]={-1m} WHERE [CompanyId]={companyId} AND [Id]={allocationId}"));
        await RejectSql("cross-tenant receipt owner", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [CashReceiptLines] SET [ReceiptId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={allocationId}"));
        await RejectSql("header with line cannot be deleted", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [CashReceipts] WHERE [CompanyId]={companyId} AND [Id]={receiptId}"));

        var after = await db.Set<CashReceipt>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == receiptId);
        var boxAfter = await db.Set<CashBox>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == cashBoxId);
        Ensure(after.Version == receipt.Version && after.Revision == receipt.Revision &&
            after.LastModifiedAt == receipt.LastModifiedAt &&
            after.Amount == receipt.Amount && after.CurrencyCode == receipt.CurrencyCode &&
            after.BranchId == receipt.BranchId && after.CashBoxId == receipt.CashBoxId &&
            boxAfter.Version == box.Version && boxAfter.Revision == box.Revision &&
            boxAfter.BalanceLimit == box.BalanceLimit &&
            await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId) == auditCount &&
            await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == companyId) == outboxCount &&
            await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == companyId) == journalCount &&
            (await db.Set<CashReceiptLine>().AsNoTracking().SingleAsync(x =>
                x.CompanyId == companyId && x.Id == allocationId)).AllocatedAmount == 30.000001m,
            "Rejected CASH03 SQL writes mutated document, box, ledger, audit or outbox");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [CashReceiptLines] WHERE [CompanyId]={companyId} AND [ReceiptId]={receiptId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [CashReceipts] WHERE [CompanyId]={companyId} AND [Id]={receiptId}");
        Ensure(!await db.Set<CashReceipt>().AnyAsync() && !await db.Set<CashReceiptLine>().AnyAsync(),
            "CASH03 disposable fixture cleanup failed");
        Console.WriteLine("  SQL PASS CASH03-01 leading zeros, exact decimals, 6 tenant FKs, atomic negative cases and no posted cash");
    }

    private static async Task RejectSql(string name, int expected, Task<int> work)
    {
        try { await work; }
        catch (SqlException ex) when (ex.Number == expected ||
            (expected == 2714 && ex.Errors.Cast<SqlError>().Any(e => e.Number == expected))) { return; }
        throw new Exception("CASH03 SQL should reject " + name);
    }

    private static async Task RejectDomain(string code, Func<Task<int>> work)
    {
        try { await work(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH03 EF must reject: " + code);
    }

    private static void Ensure(bool ok, string reason)
    {
        if (!ok) throw new Exception(reason);
    }
}
