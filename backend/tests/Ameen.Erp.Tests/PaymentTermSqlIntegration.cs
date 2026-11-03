using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class PaymentTermSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script=await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "033_payment_terms.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [PaymentTermLines] ([Conflict] int NOT NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("SYS16 conflicting schema unexpectedly succeeded");
        }
        catch(SqlException ex) when(ex.Errors.Cast<SqlError>().Any(x=>x.Number==2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>(
            "SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=33").AnyAsync(),
            "SYS16 failed schema recorded a version");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='PaymentTerms'").SingleAsync()==0,
            "SYS16 failed schema left an orphaned card table");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [PaymentTermLines]");
        // Simulate a concurrently shipped module using version 033 before
        // SYS16. A missing SYS16 table must cause an explicit rejection.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(33,SYSUTCDATETIME())");
        await Reject("schema 033 owned by another feature",
            db.Database.ExecuteSqlRawAsync(script),51002);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('PaymentTerms','PaymentTermLines')")
            .SingleAsync()==0,"SYS16 wrong-owner migration created a partial table");
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM dbo.__AmeenSchemaVersions WHERE [Version]=33");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=33").SingleAsync()==1,
            "SYS16 migration must be idempotent");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('PaymentTerms')").SingleAsync()==14 &&
            await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('PaymentTermLines')").SingleAsync()==7,
            "SYS16 original card/line SQL fields missing");
        Ensure(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('PaymentTerms'),OBJECT_ID('PaymentTermLines'))").SingleAsync()==5,
            "SYS16 company/branch/account/term foreign keys missing");
        Ensure(await db.Database.SqlQueryRaw<int>(@"SELECT COUNT(*) AS [Value]
            FROM sys.foreign_keys fk
            WHERE fk.name IN ('FK_PaymentTerm_Branch','FK_PaymentTerm_Account','FK_PaymentTermLine_Term')
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                   WHERE fc.constraint_object_id=fk.object_id)=2
              AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fc
                 JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
                 WHERE fc.constraint_object_id=fk.object_id AND c.name='CompanyId')").SingleAsync()==3,
            "SYS16 composite FKs lack tenant protection");
        Ensure(await db.Set<PaymentTerm>().CountAsync()==0 &&
            await db.Set<PaymentTermLine>().CountAsync()==0,
            "SYS16 upgrade invented business payment cards");
        var rejected=new PaymentTerm(Guid.NewGuid(),null,"BLOCK","غير فعال",
            PaymentMethodKind.Card,null,0,null,"sql-test",new DateTimeOffset(2026,11,3,0,0,0,TimeSpan.Zero));
        db.Add(rejected);
        try
        {
            await db.SaveChangesAsync();
            throw new Exception("SYS16 schema foundation accepted an unauthorized EF write");
        }
        catch(BusinessException e) when(e.Code=="payment_term_policy_unavailable") { }
        db.Entry(rejected).State=EntityState.Detached;
        Console.WriteLine("  SQL PASS SYS16-01 atomic/idempotent 033 schema, tenant FKs, empty migration and fail-closed EF writes");
    }

    public static async Task Guard(ErpDbContext db, Guid cid, Guid branchId, Guid accountId)
    {
        // Disposable SQL database only. Raw SQL exercises the structural schema
        // while EF writes are intentionally blocked until SYS16-02 authorization.
        var now = new DateTimeOffset(2026,11,3,13,0,0,TimeSpan.Zero);
        var termId=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO PaymentTerms
            (CompanyId,Id,Version,BranchId,Code,Name,Kind,SettlementAccountId,GraceDays,
             IsActive,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({cid},{termId},{Guid.NewGuid()},{branchId},{"P-0007"},{"تقسيط بطاقة"},
                   {1},{accountId},{7},{true},{"sql-schema"},{now},{1},{now})");
        foreach(var (position,percent,days) in new[] { (1,40m,0), (2,60m,30) })
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO PaymentTermLines
                  ([CompanyId],[Id],[Version],[TermId],[Position],[Percent],[DaysFromBase])
                VALUES({cid},{Guid.NewGuid()},{Guid.NewGuid()},{termId},
                       {position},{percent},{days})");

        db.ChangeTracker.Clear();
        var stored=await db.Set<PaymentTerm>().AsNoTracking().Include(x=>x.Lines)
            .SingleAsync(x=>x.CompanyId==cid && x.Id==termId);
        Ensure(stored.BranchId==branchId && stored.Code=="P-0007" &&
            stored.Kind==PaymentMethodKind.Card && stored.Name=="تقسيط بطاقة" &&
            stored.SettlementAccountId==accountId && stored.GraceDays==7 &&
            stored.Revision==1 && stored.CreatedAt==now && stored.CreatedBy=="sql-schema" &&
            stored.Lines.Count==2 && stored.Lines.Sum(x=>x.Percent)==100m &&
            stored.Lines.Sum(x=>x.DaysFromBase)==30,
            "SYS16 payment term SQL roundtrip or EF mapping diverged");
        await Reject("foreign tenant branch", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTerms SET BranchId={Guid.NewGuid()} WHERE CompanyId={cid} AND Id={termId}"),547);
        await Reject("foreign tenant settlement account", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTerms SET SettlementAccountId={Guid.NewGuid()} WHERE CompanyId={cid} AND Id={termId}"),547);
        await Reject("negative grace days", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTerms SET GraceDays={-1} WHERE CompanyId={cid} AND Id={termId}"),547);
        await Reject("unknown method kind", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTerms SET Kind={9} WHERE CompanyId={cid} AND Id={termId}"),547);
        await Reject("reversed audit time", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTerms SET LastModifiedAt={now.AddDays(-1)} WHERE CompanyId={cid} AND Id={termId}"),547);
        await Reject("invalid line percentage", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTermLines SET [Percent]={0m} WHERE CompanyId={cid} AND TermId={termId} AND Position={2}"),547);
        await Reject("negative due-date offset", db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE PaymentTermLines SET DaysFromBase={-2} WHERE CompanyId={cid} AND TermId={termId} AND Position={1}"),547);
        await Reject("duplicate line position", db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO PaymentTermLines([CompanyId],[Id],[Version],[TermId],[Position],[Percent],[DaysFromBase])
            VALUES({cid},{Guid.NewGuid()},{Guid.NewGuid()},{termId},{1},{10m},{14})"),2601,2627);
        await Reject("foreign term line", db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO PaymentTermLines([CompanyId],[Id],[Version],[TermId],[Position],[Percent],[DaysFromBase])
            VALUES({cid},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{3},{10m},{14})"),547);
        await Reject("duplicate company payment code", db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO PaymentTerms
            (CompanyId,Id,Version,BranchId,Code,Name,Kind,SettlementAccountId,GraceDays,
             IsActive,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({cid},{Guid.NewGuid()},{Guid.NewGuid()},{branchId},{"P-0007"},{"أخرى"},
                   {0},{accountId},{0},{true},{"sql-schema"},{now},{1},{now})"),2601,2627);
        db.ChangeTracker.Clear();
        var after=await db.Set<PaymentTerm>().AsNoTracking().Include(x=>x.Lines)
            .SingleAsync(x=>x.CompanyId==cid && x.Id==termId);
        Ensure(after.Version==stored.Version && after.Revision==stored.Revision &&
            after.Lines.Count==2 && after.Lines.Sum(x=>x.Percent)==100m &&
            after.Lines.All(x=>x.DaysFromBase>=0),
            "SYS16 rejected invalid SQL changed a card or installment");
        Console.WriteLine("  SQL PASS SYS16-01 tenant-scoped payment methods and installment constraints with denied cross references");
    }

    private static async Task Reject(string scope, Task operation, params int[] numbers)
    {
        try { await operation; throw new Exception("SYS16 SQL accepted "+scope); }
        catch(SqlException e) when(e.Errors.Cast<SqlError>().Any(x=>numbers.Contains(x.Number))) { }
    }

    private static void Ensure(bool value,string message)
    {
        if(!value) throw new Exception(message);
    }
}
