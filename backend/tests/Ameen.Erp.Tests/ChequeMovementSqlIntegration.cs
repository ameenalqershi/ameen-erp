using System.Data;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// CASH09 structural-only acceptance on disposable SQL. No financial action
// is actually settled, posted, approved, reversed or sent to a bank.
public static class ChequeMovementSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "037_cheque_movements_foundation.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE [ChequeMovements] ([Conflict] int NOT NULL)");
        await RejectSql("conflict must abort schema 037", 2714,
            db.Database.ExecuteSqlRawAsync(script));
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=37")
            .AnyAsync(), "Rejected CASH09 migration recorded schema version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [ChequeMovements]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=37")
            .SingleAsync() == 1, "CASH09 migration is not idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.ChequeMovements')")
            .SingleAsync() == 16, "CASH09 original fields and source resolution columns missing");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE parent_object_id=OBJECT_ID(N'dbo.ChequeMovements')")
            .SingleAsync() == 5, "CASH09 company/branch/incoming/outgoing/bank FKs missing");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.parent_object_id=OBJECT_ID(N'dbo.ChequeMovements')
              AND fk.name<>N'FK_ChequeMovement_Company'
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id=fk.object_id)=2
              AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fc
                JOIN sys.columns c ON c.object_id=fc.parent_object_id
                    AND c.column_id=fc.parent_column_id
                WHERE fc.constraint_object_id=fk.object_id AND c.name=N'CompanyId')")
            .SingleAsync() == 4, "CASH09 all source and bank FKs must be tenant-scoped");
        Ensure(await db.Database.SqlQueryRaw<int>(@"
            SELECT COUNT(*) AS [Value] FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(N'dbo.ChequeMovements')")
            .SingleAsync() == 5, "CASH09 one-cheque/amount/audit constraints missing");

        var date = new DateTimeOffset(2026,11,3,1,0,0,TimeSpan.Zero);
        var sample = new ChequeMovement(Guid.NewGuid(), null, "Unreviewed",
            Guid.NewGuid(), null, "Structural", new DateOnly(2026,11,3),
            null, null, null, "test", date);
        db.Add(sample);
        await RejectDomain("cheque_movement_policy_unavailable", () => db.SaveChangesAsync());
        db.Entry(sample).State=EntityState.Detached;
        Console.WriteLine("  SQL PASS CASH09-01 atomic/idempotent schema 037, 4 tenant composite FKs, exactly-one source, EF fail-closed");
    }

    public static async Task Guard(ErpDbContext db, Guid companyId, Guid branchId,
        Guid incomingId, Guid outgoingId, Guid bankId)
    {
        var now = new DateTimeOffset(2026,11,3,3,0,0,TimeSpan.Zero);
        var received = Guid.NewGuid();
        var issued = Guid.NewGuid();
        var fee = new SqlParameter("@fee", SqlDbType.Decimal)
            { Precision=20, Scale=6, Value=0.000001m };
        var audit = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId==companyId);
        var journal = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId==companyId);
        var outbox = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId==companyId);

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT [ChequeMovements] ([CompanyId],[Id],[Version],[BranchId],
              [State],[IncomingChequeId],[OutgoingChequeId],[Action],[ActionDate],
              [ReceivedBankAccountId],[RejectionReason],[Fee],[CreatedBy],
              [CreatedAt],[Revision],[LastModifiedAt])
            VALUES ({companyId},{received},{Guid.NewGuid()},{branchId},
              {"Unreviewed"},{incomingId},{(Guid?)null},{"Structural"},
              {new DateTime(2026,11,3)},{bankId},{"0001 reason"},
              {fee},{"test"},{now},{1},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT [ChequeMovements] ([CompanyId],[Id],[Version],[BranchId],
              [State],[IncomingChequeId],[OutgoingChequeId],[Action],[ActionDate],
              [ReceivedBankAccountId],[RejectionReason],[Fee],[CreatedBy],
              [CreatedAt],[Revision],[LastModifiedAt])
            VALUES ({companyId},{issued},{Guid.NewGuid()},{(Guid?)null},
              {"Unreviewed"},{(Guid?)null},{outgoingId},{"Structural"},
              {new DateTime(2026,11,3)},{(Guid?)null},{(string?)null},
              {(decimal?)null},{"test"},{now},{1},{now})");
        var incomingAction = await db.Set<ChequeMovement>().AsNoTracking().SingleAsync(x =>
            x.CompanyId==companyId && x.Id==received);
        var outgoingAction = await db.Set<ChequeMovement>().AsNoTracking().SingleAsync(x =>
            x.CompanyId==companyId && x.Id==issued);
        Ensure(incomingAction.ChequeId==incomingId && incomingAction.IncomingChequeId==incomingId &&
            incomingAction.OutgoingChequeId is null && incomingAction.Fee==0.000001m &&
            incomingAction.BranchId==branchId && incomingAction.ReceivedBankAccountId==bankId &&
            incomingAction.RejectionReason=="0001 reason" &&
            incomingAction.Action=="Structural" && incomingAction.State=="Unreviewed" &&
            incomingAction.ActionDate==new DateOnly(2026,11,3) &&
            incomingAction.CreatedAt==now && incomingAction.LastModifiedAt==now &&
            incomingAction.Revision==1,
            $"CASH09 incoming field roundtrip: fee={incomingAction.Fee}, id={incomingAction.ChequeId}");
        Ensure(outgoingAction.ChequeId==outgoingId && outgoingAction.IncomingChequeId is null &&
            outgoingAction.OutgoingChequeId==outgoingId &&
            outgoingAction.BranchId is null && outgoingAction.ReceivedBankAccountId is null &&
            outgoingAction.Fee is null && outgoingAction.RejectionReason is null,
            "CASH09 outgoing source and optional fields failed roundtrip");

        var unknown = Guid.NewGuid();
        await RejectSql("no cheque FK", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [IncomingChequeId]={(Guid?)null} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("cannot reference both cheque types", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [OutgoingChequeId]={outgoingId} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("unknown incoming cheque", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [IncomingChequeId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("unknown outgoing cheque", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [OutgoingChequeId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={issued}"));
        await RejectSql("foreign branch", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [BranchId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("foreign receiving bank", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [ReceivedBankAccountId]={unknown} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("negative fee", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [Fee]={-1m} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("invalid revision", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [Revision]={0} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("invalid audit time", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [LastModifiedAt]={now.AddDays(-1)} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("blank action", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [Action]={" "} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("blank state", 547, db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ChequeMovements] SET [State]={" "} WHERE [CompanyId]={companyId} AND [Id]={received}"));
        await RejectSql("incoming cheque with movement cannot be deleted", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE [IncomingCheques] WHERE [CompanyId]={companyId} AND [Id]={incomingId}"));
        await RejectSql("outgoing cheque with movement cannot be deleted", 547,
            db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE [OutgoingCheques] WHERE [CompanyId]={companyId} AND [Id]={outgoingId}"));

        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, incomingAction, "cheque_movement_policy_unavailable");
        await CashUnauthorizedEfMutationSqlIntegration.RejectUpdateAndDelete(
            db, outgoingAction, "cheque_movement_policy_unavailable");

        var after = await db.Set<ChequeMovement>().AsNoTracking().SingleAsync(x =>
            x.CompanyId==companyId && x.Id==received);
        Ensure(after.Version==incomingAction.Version && after.Revision==1 &&
            after.Fee==0.000001m && after.IncomingChequeId==incomingId &&
            after.OutgoingChequeId is null && after.Action=="Structural" &&
            await db.Set<AuditEvent>().CountAsync(x=>x.CompanyId==companyId)==audit &&
            await db.Set<JournalEntry>().CountAsync(x=>x.CompanyId==companyId)==journal &&
            await db.Set<OutboxEvent>().CountAsync(x=>x.CompanyId==companyId)==outbox,
            "CASH09 rejected SQL changed movement/cheque GL/audit/outbox");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE [ChequeMovements] WHERE [CompanyId]={companyId} AND [Id]={received}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE [ChequeMovements] WHERE [CompanyId]={companyId} AND [Id]={issued}");
        Ensure(!await db.Set<ChequeMovement>().AnyAsync(),
            "CASH09 disposable movement fixtures not removed");
        Console.WriteLine("  SQL PASS CASH09-01 exactly-one incoming/outgoing, source FK protection, audit/fee denial, zero GL/bank effect");
    }

    private static async Task RejectSql(string name, int error, Task<int> task)
    {
        try { await task; }
        catch (SqlException ex) when (ex.Number==error ||
            (error==2714 && ex.Errors.Cast<SqlError>().Any(e => e.Number==error))) { return; }
        throw new Exception("CASH09 SQL must reject: " + name);
    }

    private static async Task RejectDomain(string error, Func<Task<int>> task)
    {
        try { await task(); }
        catch (BusinessException e) when (e.Code==error) { return; }
        throw new Exception("CASH09 EF must reject: " + error);
    }

    private static void Ensure(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
    }
}
