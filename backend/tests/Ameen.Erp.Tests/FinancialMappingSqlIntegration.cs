using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class FinancialMappingSqlIntegration
{
    public static async Task Run(string connection,Guid cid,Actor owner,CreateJournalRequest example)
    {
        ErpDbContext Context()=>new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices,Task<T>> action){await using var db=Context();return await action(new TestServices(new SqlErpStore(db),new SqlTransactionRunner(db,new FixedClock()),new FixedClock()));}
        Account[] accounts;await using(var db=Context())accounts=await db.Set<Account>().Where(x=>x.CompanyId==cid && x.IsPosting && x.IsActive).OrderBy(x=>x.Code).ToArrayAsync();
        var lines=new List<FinancialMappingLineRequest>{new("ROOT","عنوان",FinancialDisplaySign.Normal)};
        for(var n=0;n<accounts.Length;n++)lines.Add(new("A"+n,"حساب "+n,FinancialDisplaySign.Normal,accounts[n].Id,"ROOT"));
        var request=new CreateFinancialMappingRequest("SQL-MAP",FinancialStatementKind.FinancialPosition,"V1",new(2026,1,1),lines.ToArray());
        var key=Fixture.Key();var creates=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Call(s=>s.FinancialMapping.Create(cid,owner,key,request,default))));
        Ensure(creates.Distinct().Count()==1,"Concurrent map creation duplicated");var created=creates[0];
        var card=await Call(s=>s.FinancialMapping.Get(cid,created.ResourceId,owner,default));Ensure(card.Lines.Count==lines.Count && card.Lines.Where(x=>x.Code!="ROOT").All(x=>x.ParentLineId==card.Lines.Single(r=>r.Code=="ROOT").Id),"SQL parent materialization failed");
        key=Fixture.Key();var body=new ReviewFinancialMappingRequest(card.Version,Fixture.Date);
        var reviews=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Call(s=>s.FinancialMapping.Review(cid,card.Id,owner,key,body,default))));Ensure(reviews.Distinct().Count()==1,"Review replay duplicated snapshots");
        var frozen=await Call(s=>s.FinancialMapping.ReviewDetail(cid,reviews[0].ResourceId,owner,default));
        decimal expected;await using(var db=Context())expected=await db.Set<JournalEntry>().Where(x=>x.CompanyId==cid && x.AccountingDate<=Fixture.Date && (x.State==JournalState.Posted || x.State==JournalState.Reversed)).SelectMany(x=>x.Lines).Where(x=>x.AccountId==accounts[0].Id).SumAsync(x=>x.FunctionalDebit-x.FunctionalCredit);
        Ensure(frozen.Evaluation.Rows.Single(x=>x.Code=="A0").Amount==expected,"SQL review differs from posted functional ledger");
        var edit=FinancialMappingTests.Edit(card,lines.AsEnumerable().Reverse().ToArray()) with{VersionCode="V2"};key=Fixture.Key();
        await using(var db=Context())await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_MapAudit] CHECK ([Action]<>N'financial-mapping.edited')");
        try
        {
            await Error("database_constraint",()=>Call(s=>s.FinancialMapping.Edit(cid,card.Id,owner,key,edit,default)));
            await using var db=Context();var old=await db.Set<FinancialMapping>().SingleAsync(x=>x.CompanyId==cid && x.Id==card.Id);
            Ensure(old.Version==card.Version && old.Revision==1 && old.VersionCode=="V1" && old.Lines.Count==lines.Count && !await db.Set<IdempotencyRecord>().AnyAsync(x=>x.CompanyId==cid && x.Key==key),"Failed edit leaked map/lines/revision/receipt");
        }
        finally{await using var db=Context();await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_MapAudit]");}
        CommandReceipt edited;
        await using(var restricted=new SqlConnection(connection))
        {
            await restricted.OpenAsync();await using var cmd=restricted.CreateCommand();cmd.CommandText="EXECUTE AS USER='ameen_runtime_test'";await cmd.ExecuteNonQueryAsync();
            try
            {
                await using var db=new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(restricted).Options);
                var s=new TestServices(new SqlErpStore(db),new SqlTransactionRunner(db,new FixedClock()),new FixedClock());
                edited=await s.FinancialMapping.Edit(cid,card.Id,owner,key,edit,default);
                cmd.CommandText="UPDATE [FinancialMappingReviews] SET [Ready]=[Ready] WHERE 1=0";
                try{await cmd.ExecuteNonQueryAsync();throw new Exception("Runtime rewrites map review");}catch(SqlException ex)when(ex.Number==229){}
            }
            finally{cmd.CommandText="REVERT";await cmd.ExecuteNonQueryAsync();}
        }
        Ensure(edited==await Call(s=>s.FinancialMapping.Edit(cid,card.Id,owner,key,edit,default)),"Runtime edit replay differs");
        await Error("version_conflict",()=>Call(s=>s.FinancialMapping.Edit(cid,card.Id,owner,Fixture.Key(),edit,default)));
        var after=await Call(s=>s.FinancialMapping.Get(cid,card.Id,owner,default));Ensure(after.Revision==2 && after.Lines.Count==lines.Count && after.Lines.Single(x=>x.Position==1).Code==lines.Last().Code,"Line replacement/revision differs");
        var oldReview=await Call(s=>s.FinancialMapping.ReviewDetail(cid,reviews[0].ResourceId,owner,default));Ensure(oldReview.MappingVersion==card.Version && oldReview.Evaluation.VersionCode=="V1","Edited map rewrote old review");
        var page=await Call(s=>s.FinancialMapping.Search(cid,owner,null,FinancialStatementKind.FinancialPosition,true,"SQL-MAP",0,1,default));Ensure(page.Items.Single().Id==card.Id,"SQL map search translation failed");
        Ensure((await Call(s=>s.FinancialMapping.Usage(cid,card.Id,owner,0,1,default))).TotalCount==1,"Usage does not reflect immutable review");
        key=Fixture.Key();await using(var db=Context())await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_MapReviewAudit] CHECK ([Action]<>N'financial-mapping.reviewed')");
        try
        {
            await Error("database_constraint",()=>Call(s=>s.FinancialMapping.Review(cid,card.Id,owner,key,new(edited.Version,Fixture.Date),default)));
            await using var db=Context();Ensure(await db.Set<FinancialMappingReview>().CountAsync(x=>x.CompanyId==cid && x.MappingId==card.Id)==1 && !await db.Set<IdempotencyRecord>().AnyAsync(x=>x.CompanyId==cid && x.Key==key),"Failed review leaked snapshot/receipt");
        }
        finally{await using var db=Context();await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_MapReviewAudit]");}
        await Call(s=>s.FinancialMapping.Review(cid,card.Id,owner,key,new(edited.Version,Fixture.Date),default));
        await using(var db=Context())
        {
            var review=await db.Set<FinancialMappingReview>().SingleAsync(x=>x.CompanyId==cid && x.Id==reviews[0].ResourceId);review.Touch();await Error("mapping_review_immutable",()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var row=await db.Set<FinancialMappingLine>().FirstAsync(x=>x.CompanyId==cid && x.MappingId==card.Id && x.AccountId!=null);
            var foreignAccount=await db.Set<Account>().Where(x=>x.CompanyId!=cid).Select(x=>x.Id).FirstAsync();
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [FinancialMappingLines] SET [AccountId]={foreignAccount} WHERE [CompanyId]={cid} AND [Id]={row.Id}");throw new Exception("Cross-company/account FK absent");}catch(SqlException ex)when(ex.Number==547){}
            try{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [FinancialMappingLines] SET [DisplaySign]=0 WHERE [CompanyId]={cid} AND [Id]={row.Id}");throw new Exception("Invalid sign accepted by SQL");}catch(SqlException ex)when(ex.Number==547){}
        }
        Console.WriteLine("  SQL PASS GL13 concurrent replay, parent-line replacement under runtime role, posted ledger review, immutable snapshots, tenant FKs, audit rollback and retry");
    }
    private static void Ensure(bool ok,string why){if(!ok)throw new Exception(why);}
    private static async Task Error(string code,Func<Task> action){try{await action();}catch(BusinessException e)when(e.Code==code){return;}throw new Exception("Expected "+code);}
}
