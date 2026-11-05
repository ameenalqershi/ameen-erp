using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// CASH05 schema acceptance runs ONLY inside SqlIntegration's disposable SQL database.
// No operational transfer, settlement, balance reservation or GL journal is authorized.
public static class CashTransferSqlIntegration
{
    private const string Script = "049_cash_transfers_foundation.sql";

    public static async Task Upgrade(ErpDbContext db)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "database", Script));
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE [CashTransfers] ([Conflict] int NOT NULL)");
        await RejectSql("CASH05 schema conflict must be atomic", 2714,
            db.Database.ExecuteSqlRawAsync(sql));
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49")==0,
            "CASH05 failed migration recorded a schema receipt");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [CashTransfers]");

        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(49,SYSUTCDATETIME())");
        await RejectSql("CASH05 schema version claimed by unrelated module", 51002,
            db.Database.ExecuteSqlRawAsync(sql));
        await db.Database.ExecuteSqlRawAsync(
            "DELETE dbo.__AmeenSchemaVersions WHERE [Version]=49");

        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync(sql);
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=49")==1,
            "CASH05 schema migration was not idempotent");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CashTransfers')")==30,
            "CASH05 original fields, typed endpoint aliases or concurrency fields missing");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.CashTransfers')")==5,
            "CASH05 field/draft/typed endpoint guards missing");
        Ensure(await Count(db,
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.CashTransfers')")==9,
            "CASH05 must have company/branch/year/two currency/four endpoint FKs");
        Ensure(await Count(db, @"
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.parent_object_id=OBJECT_ID(N'dbo.CashTransfers')
              AND fk.name<>N'FK_CashTransfer_Company'
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns col
                   WHERE col.constraint_object_id=fk.object_id)=2
              AND EXISTS (
                SELECT 1 FROM sys.foreign_key_columns fc
                JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
                WHERE fc.constraint_object_id=fk.object_id AND c.name=N'CompanyId')
        ")==8, "CASH05 all original references must be tenant-scoped");

        var type = db.Model.FindEntityType(typeof(CashTransfer)) ??
            throw new Exception("CASH05 EF entity missing");
        Ensure(type.FindPrimaryKey()?.Properties.Select(x => x.Name).SequenceEqual(
            new[] { nameof(CashTransfer.CompanyId), nameof(CashTransfer.Id) })==true,
            "CASH05 missing composite tenant key");
        Ensure(type.FindProperty(nameof(CashTransfer.Version))?.IsConcurrencyToken==true,
            "CASH05 optimistic version disabled");
        Ensure(await db.Set<CashTransfer>().CountAsync()==0,
            "CASH05 migration created fictitious financial transfers");

        // An EF caller must never be able to persist even a valid-looking Draft.
        var a = new CashTransferAccountRef(CashTransferAccountKind.CashBox, Guid.NewGuid());
        var b = new CashTransferAccountRef(CashTransferAccountKind.BankAccount, Guid.NewGuid());
        var invalid = new CashTransfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "00001", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5),
            "SAR", 1m, "test transfer", null, "Draft", a, b,
            1m, 1m, "SAR", 1m, null, "test", DateTimeOffset.UtcNow);
        db.Add(invalid);
        await RejectDomain("cash_transfer_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(invalid).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH05-01 atomic/idempotent 049, typed tenant FKs and blocked EF writes");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId,
        Guid branchId, Guid sourceCashBoxId, Guid ledgerAccountId, string actor)
    {
        var year = await db.Set<FiscalYear>().Where(x => x.CompanyId==companyId)
            .Select(x => x.Id).FirstAsync();
        var bankId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

        // The source cashbox exists from CASH01 SQL acceptance; the bank fixture
        // is a schema-only row and is removed before the disposable DB is dropped.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dbo.BankAccounts
            (CompanyId,Id,Version,Code,Name,IsActive,BankName,AccountNumber,
             LedgerAccountId,CurrencyCode,BranchId,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{bankId},{Guid.NewGuid()},{"CASH05-BANK"},
              {"بنك اختبار التحويل"},{true},{"بنك اختبار"},{"00750001"},
              {ledgerAccountId},{"SAR"},{branchId},{actor},{created},{1},{created})");

        var sourceRate = new SqlParameter("@sourceRate", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 10, Value = 1.1234567890m };
        var targetRate = new SqlParameter("@targetRate", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 10, Value = 1.0000000001m };
        var sourceAmount = new SqlParameter("@sourceAmount", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 100.123456m };
        var targetAmount = new SqlParameter("@targetAmount", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 100.123456m };
        var fee = new SqlParameter("@fee", System.Data.SqlDbType.Decimal)
            { Precision = 20, Scale = 6, Value = 0.000001m };

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dbo.CashTransfers
            (CompanyId,Id,Version,BranchId,FiscalYearId,Number,DocumentDate,AccountingDate,
             CurrencyCode,ExchangeRate,Description,ExternalReference,State,
             SourceKind,SourceAccountId,DestinationKind,DestinationAccountId,
             SourceAmount,DestinationAmount,DestinationCurrencyCode,DestinationExchangeRate,Fee,
             SourceCashBoxId,SourceBankAccountId,DestinationCashBoxId,DestinationBankAccountId,
             CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{transferId},{Guid.NewGuid()},{branchId},{year},{"0000007"},
              {new DateOnly(2026,11,5)},{new DateOnly(2026,11,5)},
              {"SAR"},{sourceRate},{"اختبار تحويل مقفل"},{"CASH05-EXT"},{"Draft"},
              {0},{sourceCashBoxId},{1},{bankId},{sourceAmount},{targetAmount},
              {"SAR"},{targetRate},{fee},
              {sourceCashBoxId},NULL,NULL,{bankId},
              {actor},{created},{1},{created})");
        db.ChangeTracker.Clear();
        var saved = await db.Set<CashTransfer>().AsNoTracking().SingleAsync(x=>
            x.CompanyId==companyId && x.Id==transferId);
        Ensure(saved.Number=="0000007" && saved.CreatedBy==actor &&
            saved.SourceKind==CashTransferAccountKind.CashBox &&
            saved.DestinationKind==CashTransferAccountKind.BankAccount &&
            saved.SourceCashBoxId==sourceCashBoxId && saved.SourceBankAccountId==null &&
            saved.DestinationCashBoxId==null && saved.DestinationBankAccountId==bankId &&
            saved.SourceAmount==100.123456m && saved.DestinationAmount==100.123456m &&
            saved.ExchangeRate==1.1234567890m && saved.DestinationExchangeRate==1.0000000001m &&
            saved.Fee==0.000001m && saved.ExternalReference=="CASH05-EXT" &&
            saved.CreatedAt==created && saved.LastModifiedAt==created &&
            saved.Revision==1 && saved.State=="Draft",
            "CASH05 SQL/EF roundtrip lost original header, monetary precision or typed FK aliases");

        // None of these rejected direct SQL changes may alter the original row.
        await RejectSql("source cashbox foreign reference", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET SourceAccountId={Guid.NewGuid()},
                SourceCashBoxId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("destination bank foreign reference", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET DestinationAccountId={Guid.NewGuid()},
                DestinationBankAccountId={Guid.NewGuid()} WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("mismatched endpoint alias", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET SourceCashBoxId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("same source and destination", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET DestinationKind={0},
                DestinationAccountId={sourceCashBoxId},
                DestinationCashBoxId={sourceCashBoxId},DestinationBankAccountId=NULL
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("foreign branch", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET BranchId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("foreign currency", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET DestinationCurrencyCode={"ZZZ"}
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("unapproved posted status", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET State={"Posted"}
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("negative fee", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET Fee={-1m}
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("non-positive revision", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET Revision={0}
              WHERE CompanyId={companyId} AND Id={transferId}"));
        await RejectSql("backdated audit", 547,
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.CashTransfers SET LastModifiedAt={created.AddDays(-1)}
              WHERE CompanyId={companyId} AND Id={transferId}"));

        var originalAudits = await db.Set<AuditEvent>().CountAsync();
        var originalOutbox = await db.Set<OutboxEvent>().CountAsync();
        var originalJournals = await db.Set<JournalEntry>().CountAsync();
        db.Attach(saved);
        db.Entry(saved).State=EntityState.Modified;
        await RejectDomain("cash_transfer_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(saved).State=EntityState.Detached;
        db.Attach(saved);
        db.Entry(saved).State=EntityState.Deleted;
        await RejectDomain("cash_transfer_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(saved).State=EntityState.Detached;

        var unchanged = await db.Set<CashTransfer>().AsNoTracking()
            .SingleAsync(x=>x.CompanyId==companyId && x.Id==transferId);
        Ensure(unchanged.Revision==1 && unchanged.State=="Draft" &&
            unchanged.Version==saved.Version && unchanged.LastModifiedAt==created &&
            unchanged.SourceAccountId==sourceCashBoxId &&
            unchanged.DestinationAccountId==bankId &&
            await db.Set<AuditEvent>().CountAsync()==originalAudits &&
            await db.Set<OutboxEvent>().CountAsync()==originalOutbox &&
            await db.Set<JournalEntry>().CountAsync()==originalJournals,
            "CASH05 rejected SQL/EF changes affected header/audit/journal/outbox");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE dbo.CashTransfers WHERE CompanyId={companyId} AND Id={transferId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE dbo.BankAccounts WHERE CompanyId={companyId} AND Id={bankId}");
        Console.WriteLine("  SQL PASS CASH05-01 typed endpoints, tenant FKs, fixed decimals, fail-closed SQL/EF, no GL effects");
    }

    private static async Task<int> Count(ErpDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    private static async Task RejectSql(string name, int code, Task action)
    {
        try { await action; throw new Exception("CASH05 rejected SQL accepted: " + name); }
        catch(SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e=>e.Number==code)) { }
    }

    private static async Task RejectDomain(string code, Func<Task> action)
    {
        try { await action(); throw new Exception("CASH05 unauthorized EF write accepted"); }
        catch(BusinessException ex) when(ex.Code==code) { }
    }

    private static void Ensure(bool ok, string reason)
    {
        if(!ok) throw new Exception(reason);
    }
}
