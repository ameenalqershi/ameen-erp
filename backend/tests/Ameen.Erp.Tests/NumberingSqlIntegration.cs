using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class NumberingSqlIntegration
{
    public static async Task Run(string connection,Guid cid,Actor owner,CreateJournalRequest example)
    {
        ErpDbContext Context()=>new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices,Task<T>> action){await using var db=Context();return await action(new TestServices(new SqlErpStore(db),new SqlTransactionRunner(db,new FixedClock()),new FixedClock()));}
        var actor=new Actor("numbering-integration");
        await Call(s=>s.Membership.GrantMembership(cid,owner,Fixture.Key(),new(actor.Subject,Permissions.All,true,Array.Empty<Guid>(),null),default));
        FiscalPeriod period;await using(var db=Context())period=await db.Set<FiscalPeriod>().Where(x=>x.CompanyId==cid && !x.IsClosed).OrderBy(x=>x.StartsOn).FirstAsync();
        var request=example with {AccountingDate=period.StartsOn,DocumentDate=period.StartsOn};
        var card=await Call(s=>s.Numbering.Create(cid,actor,Fixture.Key(),new(NumberingDocumentKind.Journal,period.FiscalYearId,100,"SQLNUM-"),default));
        var key=Fixture.Key();var body=new ReserveDocumentNumberRequest(card.Version,request.BranchId);
        var replays=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Call(s=>s.Numbering.Reserve(cid,card.ResourceId,actor,key,body,default))));
        Ensure(replays.Distinct().Count()==1 && replays[0].Number=="SQLNUM-000100","Concurrent reserve did not replay one number");
        var documents=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Call(s=>s.Journal.CreateJournal(cid,actor,Fixture.Key(),request,default))));
        Ensure(documents.Select(x=>x.Number).Distinct().Count()==8,"Independent SQL journal creates repeated configured number");
        var cancelled=await Call(s=>s.Journal.TransitionJournal(cid,documents[0].ResourceId,actor,Fixture.Key(),new(documents[0].Version,"إلغاء مفحوص"),"cancel",default));
        Ensure(cancelled.Status=="Cancelled","Cancellation failed");
        var current=await Call(s=>s.Numbering.Get(cid,card.ResourceId,actor,default));
        Ensure(current.NextNumber==109 && current.Revision==10,"Configured next/revision differs from committed allocations");
        var auditDdl="ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_NumberAudit] CHECK ([ResourceId]<>'"+card.ResourceId.ToString("D")+"' OR [Action]<>'numbering.allocated')";
        await using(var db=Context())await db.Database.ExecuteSqlRawAsync(auditDdl);
        key=Fixture.Key();
        await Error("database_constraint",()=>Call(s=>s.Journal.CreateJournal(cid,actor,key,request,default)));
        await using(var db=Context())
        {
            var unchanged=await db.Set<DocumentNumberSeries>().SingleAsync(x=>x.CompanyId==cid && x.Id==card.ResourceId);
            Ensure(unchanged.Version==current.Version && unchanged.NextNumber==109 && unchanged.Revision==10,"Audit failure consumed a configured number");
            Ensure(await db.Set<DocumentNumberAllocation>().CountAsync(x=>x.CompanyId==cid && x.SeriesId==card.ResourceId)==9,"Audit failure leaked reservation");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x=>x.CompanyId==cid && x.Key==key),"Audit failure retained receipt");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_NumberAudit]");
        }
        var retry=await Call(s=>s.Journal.CreateJournal(cid,actor,key,request,default));Ensure(retry.Number=="SQLNUM-000109","Retry consumed incorrect number");
        var usage=await Call(s=>s.Numbering.Usage(cid,card.ResourceId,actor,0,100,default));
        Ensure(usage.TotalCount==10 && usage.Items.Count(x=>x.DocumentId is not null)==9 && usage.Items.Any(x=>x.DocumentId==cancelled.ResourceId && x.DocumentState=="Cancelled"),"Usage lost cancelled/committed document links");
        await using(var db=Context())
        {
            var a=await db.Set<DocumentNumberAllocation>().FirstAsync(x=>x.CompanyId==cid && x.SeriesId==card.ResourceId);a.Touch();
            await Error("number_allocation_immutable",()=>db.SaveChangesAsync());
        }
        await using(var sql=new SqlConnection(connection))
        {
            await sql.OpenAsync();await using var command=sql.CreateCommand();command.CommandText="EXECUTE AS USER='ameen_runtime_test'";await command.ExecuteNonQueryAsync();
            try{command.CommandText="UPDATE [DocumentNumberAllocations] SET [ReservedBy]=[ReservedBy] WHERE 1=0";try{await command.ExecuteNonQueryAsync();throw new Exception("Runtime role can rewrite reserved numbers");}catch(SqlException ex)when(ex.Number==229){}}
            finally{command.CommandText="REVERT";await command.ExecuteNonQueryAsync();}
        }
        // SYS10-03: scope/tenant guards must reject without consuming allocations or receipts.
        var isolatedBranch=await Call(s=>s.Branch.CreateBranch(cid,owner,Fixture.Key(),
            new("SQL-SYS10-SEC","فرع معزول","مدينة","عنوان"),default));
        var scoped=await Call(s=>s.Numbering.Create(cid,owner,Fixture.Key(),
            new(NumberingDocumentKind.InventoryOpening,period.FiscalYearId,2000,"SEC-",isolatedBranch.ResourceId),default));
        var scopedReader=new Actor("sys10-sql-scope-reader");
        await Call(s=>s.Membership.GrantMembership(cid,owner,Fixture.Key(),
            new(scopedReader.Subject,new[]{Permissions.Read},false,new[]{isolatedBranch.ResourceId},null),default));
        var own=await Call(s=>s.Numbering.Get(cid,scoped.ResourceId,scopedReader,default));
        Ensure(own.Id==scoped.ResourceId,"SQL scoped reader cannot view own numbering series");
        int beforeAudit,beforeReceipts,beforeAllocations;
        await using(var db=Context())
        {
            beforeAudit=await db.Set<AuditEvent>().CountAsync();
            beforeReceipts=await db.Set<IdempotencyRecord>().CountAsync();
            beforeAllocations=await db.Set<DocumentNumberAllocation>().CountAsync();
        }
        await Error("all_branches_required",()=>Call(s=>s.Numbering.Get(cid,card.ResourceId,scopedReader,default)));
        await Error("permission_denied",()=>Call(s=>s.Numbering.Get(Guid.NewGuid(),scoped.ResourceId,scopedReader,default)));
        await Error("permission_denied",()=>Call(s=>s.Numbering.Changes(cid,scoped.ResourceId,scopedReader,0,20,default)));
        await Error("permission_denied",()=>Call(s=>s.Numbering.Reserve(cid,scoped.ResourceId,scopedReader,Fixture.Key(),
            new ReserveDocumentNumberRequest(scoped.Version,isolatedBranch.ResourceId),default)));
        await using(var db=Context())
        {
            var unchanged=await db.Set<DocumentNumberSeries>().SingleAsync(x=>x.Id==scoped.ResourceId && x.CompanyId==cid);
            Ensure(unchanged.NextNumber==2000 && unchanged.Version==scoped.Version &&
                await db.Set<AuditEvent>().CountAsync()==beforeAudit &&
                await db.Set<IdempotencyRecord>().CountAsync()==beforeReceipts &&
                await db.Set<DocumentNumberAllocation>().CountAsync()==beforeAllocations,
                "SYS10 SQL denied tenant/branch operations modified series, receipts, audit or allocations");
        }
        Console.WriteLine("  SQL PASS SYS10-03 branch/tenant isolation, separated read/audit/allocate permissions and rollback");
        Console.WriteLine("  SQL PASS SYS10 scope configuration, concurrent reserve/create, cancellation, immutable history, audit rollback and exact retry");
    }
    private static void Ensure(bool ok,string why){if(!ok)throw new Exception(why);}
    private static async Task Error(string code,Func<Task> action){try{await action();}catch(BusinessException e)when(e.Code==code){return;}throw new Exception("Expected "+code);}
}
