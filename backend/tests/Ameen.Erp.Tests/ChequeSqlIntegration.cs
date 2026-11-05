using System.Data;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// Only disposable SQL schema and invariant acceptance: CASH07/CASH08 Cheque
// lifecycle and accounting remain forbidden through EF/API.
// Only two hardcoded SQL table names are interpolated as identifiers in test-only
// schema probes. The inputs are never supplied by a user or production API.
#pragma warning disable EF1002
public static class ChequeSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        await UpgradeOne(db, "035_incoming_cheques_foundation.sql", "IncomingCheques", 35);
        await UpgradeOne(db, "036_outgoing_cheques_foundation.sql", "OutgoingCheques", 36);
        foreach (var table in new[] { "IncomingCheques", "OutgoingCheques" })
        {
            Ensure(await db.Database.SqlQueryRaw<int>($@"
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE object_id=OBJECT_ID(N'dbo.{table}')").SingleAsync() == 23,
                $"{table} must persist 22 original fields plus concurrency Version");
            Ensure(await db.Database.SqlQueryRaw<int>($@"
                SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
                WHERE parent_object_id=OBJECT_ID(N'dbo.{table}')").SingleAsync() == 5,
                $"{table} must have 5 company-scoped references");
            Ensure(await db.Database.SqlQueryRaw<int>($@"
                SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
                WHERE fk.parent_object_id=OBJECT_ID(N'dbo.{table}')
                  AND fk.name<>N'FK_{(table == "IncomingCheques" ? "IncomingCheque" : "OutgoingCheque")}_Company'
                  AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                       WHERE fc.constraint_object_id=fk.object_id)=2
                  AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                      JOIN sys.columns col ON col.object_id=fc.parent_object_id
                          AND col.column_id=fc.parent_column_id
                      WHERE fc.constraint_object_id=fk.object_id
                        AND col.name=N'CompanyId')").SingleAsync() == 4,
                $"{table} FK must include tenant CompanyId");
            Ensure(await db.Database.SqlQueryRaw<int>($@"
                SELECT COUNT(*) AS [Value] FROM sys.check_constraints
                WHERE parent_object_id=OBJECT_ID(N'dbo.{table}')").SingleAsync() ==
                (table == "IncomingCheques" ? 5 : 6),
                $"{table} audit/rate/amount/state checks missing");
        }

        var created = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var incoming = new IncomingCheque(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "0001", new(2026,11,1), new(2026,11,2), "SAR", 1m,
            "cheque", null, "Unreviewed", "000001", "Bank", Guid.NewGuid(),
            1m, new(2026,12,1), Guid.NewGuid(), "test", created);
        db.Add(incoming);
        await RejectDomain("cheque_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(incoming).State = EntityState.Detached;

        var outgoing = new OutgoingCheque(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "0002", new(2026,11,1), new(2026,11,2), "SAR", 1m,
            "cheque", null, "Unreviewed", "000002", Guid.NewGuid(),
            Guid.NewGuid(), 1m, new(2026,12,1), "Unreviewed", "test", created);
        db.Add(outgoing);
        await RejectDomain("cheque_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(outgoing).State = EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH07/CASH08 schemas 035/036 atomic, idempotent, 22 source fields, company FKs and EF fail-closed");
    }

    private static async Task UpgradeOne(ErpDbContext db, string file, string table, int version)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", file));
        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE [{table}] ([Conflict] int NOT NULL)");
        await RejectSql(table + " conflict rolls back schema", 2714,
            db.Database.ExecuteSqlRawAsync(script));
        Ensure(!await db.Database.SqlQueryRaw<int>(
            $"SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]={version}")
            .AnyAsync(), $"{table} recorded failed migration version");
        await db.Database.ExecuteSqlRawAsync($"DROP TABLE [{table}]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]={version}")
            .SingleAsync() == 1, $"{table} schema was not idempotent");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId,
        Guid accountId)
    {
        var fiscalYearId = await db.Set<FiscalYear>().Where(x => x.CompanyId == companyId)
            .Select(x => x.Id).FirstAsync();
        var audit = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId);
        var journal = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == companyId);
        var outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == companyId);
        var now = new DateTimeOffset(2026,11,3,2,0,0,TimeSpan.Zero);
        var rate = Decimal("@rate", 10, 1.1234567890m);
        var amount = Decimal("@amount", 6, 500.000001m);
        var incomingId = Guid.NewGuid();
        var payerId = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT [IncomingCheques] ([CompanyId],[Id],[Version],[BranchId],[FiscalYearId],
              [Number],[DocumentDate],[AccountingDate],[CurrencyCode],[ExchangeRate],
              [Description],[ExternalReference],[State],[ChequeNumber],[DrawnBankName],
              [PayerId],[Amount],[DueDate],[CollectionAccountId],[CreatedBy],
              [CreatedAt],[Revision],[LastModifiedAt])
            VALUES ({companyId},{incomingId},{Guid.NewGuid()},{branchId},{fiscalYearId},
              {"0000010"},{new DateTime(2026,11,1)},{new DateTime(2026,11,2)},{"SAR"},
              {rate},{"isolated SQL cheque"},{"00012"},{"Unreviewed"},{"000001234"},
              {"Drawn bank"},{payerId},{amount},{new DateTime(2026,12,1)},{accountId},
              {"structural"},{now},{1},{now})");
        var incoming = await db.Set<IncomingCheque>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == incomingId);
        Ensure(incoming.Number=="0000010" && incoming.ChequeNumber=="000001234" &&
            incoming.ExternalReference=="00012" && incoming.PayerId==payerId &&
            incoming.CollectionAccountId==accountId && incoming.Amount==500.000001m &&
            incoming.ExchangeRate==1.1234567890m && incoming.BranchId==branchId &&
            incoming.FiscalYearId==fiscalYearId && incoming.CurrencyCode=="SAR" &&
            incoming.Revision==1 && incoming.CreatedAt==now &&
            incoming.LastModifiedAt==now && incoming.DueDate==new DateOnly(2026,12,1),
            $"CASH07 SQL roundtrip: amount={incoming.Amount},rate={incoming.ExchangeRate},num={incoming.ChequeNumber}");

        var unknown = Guid.NewGuid();
        await RejectSql("incoming foreign branch", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [BranchId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming foreign year", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [FiscalYearId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming foreign collection account", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [CollectionAccountId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming foreign currency", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [CurrencyCode]={"ZZZ"} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming negative money", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [Amount]={0m} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming invalid revision", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming audit backdating", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [LastModifiedAt]={now.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("incoming blank cheque number", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [IncomingCheques] SET [ChequeNumber]={"  "} WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));

        // CASH08 requires an existing bank-account master card. Insert a
        // disposable bank fixture directly into disposable SQL only; EF bank
        // writes remain denied and NO financial balance is manipulated.
        var bankId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT [BankAccounts] ([CompanyId],[Id],[Version],[Code],[Name],
              [EnglishName],[IsActive],[Notes],[BankName],[AccountNumber],[Iban],
              [LedgerAccountId],[CurrencyCode],[BranchId],[CreatedBy],[CreatedAt],
              [Revision],[LastModifiedAt])
            VALUES ({companyId},{bankId},{Guid.NewGuid()},{"CHEQ-FIXTURE"},{"Bank fixture"},
              {(string?)null},{true},{(string?)null},{"Test Bank"},{"0000000005"},
              {(string?)null},{accountId},{"SAR"},{branchId},{"test"},{now},{1},{now})");
        var bank = await db.Set<BankAccount>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == bankId);
        var outgoingId = Guid.NewGuid();
        var beneficiaryId = Guid.NewGuid();
        var outgoingRate = Decimal("@outRate", 10, 1.1234567890m);
        var outgoingAmount = Decimal("@outAmount", 6, 125.000001m);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT [OutgoingCheques] ([CompanyId],[Id],[Version],[BranchId],[FiscalYearId],
              [Number],[DocumentDate],[AccountingDate],[CurrencyCode],[ExchangeRate],
              [Description],[ExternalReference],[State],[ChequeNumber],[BankAccountId],
              [BeneficiaryId],[Amount],[DueDate],[ChequeState],[CreatedBy],[CreatedAt],
              [Revision],[LastModifiedAt])
            VALUES ({companyId},{outgoingId},{Guid.NewGuid()},{branchId},{fiscalYearId},
              {"0000020"},{new DateTime(2026,11,1)},{new DateTime(2026,11,2)},{"SAR"},
              {outgoingRate},{"isolated SQL outgoing cheque"},{"00023"},{"Unreviewed"},
              {"000000987"},{bankId},{beneficiaryId},{outgoingAmount},
              {new DateTime(2026,12,1)},{"Unreviewed"},{"structural"},{now},{1},{now})");
        var outgoing = await db.Set<OutgoingCheque>().AsNoTracking().SingleAsync(x =>
            x.CompanyId == companyId && x.Id == outgoingId);
        Ensure(outgoing.Number=="0000020" && outgoing.ChequeNumber=="000000987" &&
            outgoing.ExternalReference=="00023" && outgoing.BeneficiaryId==beneficiaryId &&
            outgoing.BankAccountId==bankId && outgoing.ChequeState=="Unreviewed" &&
            outgoing.Amount==125.000001m && outgoing.ExchangeRate==1.1234567890m &&
            outgoing.Revision==1 && outgoing.CreatedAt==now &&
            outgoing.LastModifiedAt==now && outgoing.DueDate==new DateOnly(2026,12,1),
            $"CASH08 SQL roundtrip: amount={outgoing.Amount},rate={outgoing.ExchangeRate},num={outgoing.ChequeNumber}");

        await RejectSql("outgoing foreign bank", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [BankAccountId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing foreign branch", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [BranchId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing foreign year", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [FiscalYearId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing foreign currency", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [CurrencyCode]={"ZZZ"} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing invalid rate", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [ExchangeRate]={0m} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing invalid money", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [Amount]={-1m} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing cheque state blank", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [ChequeState]={" "} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));
        await RejectSql("outgoing audit backdating", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [OutgoingCheques] SET [LastModifiedAt]={now.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));

        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, incoming, "cheque_policy_unavailable");
        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, outgoing, "cheque_policy_unavailable");

        var outgoingAfter = await db.Set<OutgoingCheque>().AsNoTracking().SingleAsync(x =>
            x.CompanyId==companyId && x.Id==outgoingId);
        var incomingAfter = await db.Set<IncomingCheque>().AsNoTracking().SingleAsync(x =>
            x.CompanyId==companyId && x.Id==incomingId);
        var bankAfter = await db.Set<BankAccount>().AsNoTracking().SingleAsync(x =>
            x.CompanyId==companyId && x.Id==bankId);
        Ensure(outgoingAfter.Version==outgoing.Version &&
            outgoingAfter.ChequeState==outgoing.ChequeState &&
            outgoingAfter.Amount==outgoing.Amount &&
            incomingAfter.Amount==incoming.Amount &&
            incomingAfter.CollectionAccountId==incoming.CollectionAccountId &&
            bankAfter.Version==bank.Version && bankAfter.Revision==bank.Revision &&
            await db.Set<AuditEvent>().CountAsync(x => x.CompanyId==companyId)==audit &&
            await db.Set<JournalEntry>().CountAsync(x => x.CompanyId==companyId)==journal &&
            await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId==companyId)==outbox,
            "Rejected cheque SQL modified bank, accounting, audit or journal");
        // The CASH09 FK acceptance runs against these disposable parent
        // cheque fixtures before they are removed.
        await ChequeMovementSqlIntegration.Guard(
            db, companyId, branchId, incomingId, outgoingId, bankId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [OutgoingCheques] WHERE [CompanyId]={companyId} AND [Id]={outgoingId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [BankAccounts] WHERE [CompanyId]={companyId} AND [Id]={bankId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [IncomingCheques] WHERE [CompanyId]={companyId} AND [Id]={incomingId}");
        Ensure(!await db.Set<IncomingCheque>().AnyAsync() &&
            !await db.Set<OutgoingCheque>().AnyAsync(),
            "Disposable cheque fixtures not removed");
        Console.WriteLine("  SQL PASS CASH07/CASH08 original field roundtrip, decimal precision, tenant FK denial and zero financial impact");
    }

    private static SqlParameter Decimal(string name, byte scale, decimal value) =>
        new(name, SqlDbType.Decimal) { Precision=20, Scale=scale, Value=value };

    private static async Task RejectSql(string label, int code, Task<int> work)
    {
        try { await work; }
        catch (SqlException ex) when (ex.Number==code ||
            (code==2714 && ex.Errors.Cast<SqlError>().Any(e => e.Number==code))) { return; }
        throw new Exception("CASH cheque SQL should reject: " + label);
    }

    private static async Task RejectDomain(string code, Func<Task<int>> work)
    {
        try { await work(); }
        catch (BusinessException ex) when (ex.Code==code) { return; }
        throw new Exception("CASH cheque EF should reject " + code);
    }

    private static void Ensure(bool value, string error)
    {
        if (!value) throw new Exception(error);
    }
}

#pragma warning restore EF1002
