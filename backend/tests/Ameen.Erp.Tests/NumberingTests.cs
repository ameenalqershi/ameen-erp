using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class NumberingTests
{
    private static Guid Year(Fixture f)=>f.Store.All<FiscalYear>().Single().Id;
    private static CreateNumberSeriesRequest Request(Fixture f,Guid? branch=null)=>new(NumberingDocumentKind.Journal,Year(f),10,"CFG-",branch);
    private static Task<CommandReceipt> Create(Fixture f,CreateNumberSeriesRequest? r=null,string? key=null)=>f.Service.Numbering.Create(f.CompanyId,f.Owner,key??Fixture.Key(),r??Request(f),default);
    public static void Register(Action<string,Func<Fixture,Task>> test)
    {
        test("SYS10 fields: optional scope/prefix, explicit type/year/next and server audit fields match source",async f=>
        {
            var json=$"{{\"documentKind\":\"Journal\",\"fiscalYearId\":\"{Year(f)}\",\"nextNumber\":5}}";
            var r=JsonSerializer.Deserialize<CreateNumberSeriesRequest>(json,JsonDefaults.Options)!;
            var saved=await Create(f,r);var c=await f.Service.Numbering.Get(f.CompanyId,saved.ResourceId,f.Owner,default);
            Ensure(c.CompanyId==f.CompanyId && c.BranchId is null && c.Prefix is null && c.NextNumber==5 && c.IsActive &&
                c.Revision==1 && c.CreatedBy==f.Owner.Subject && c.LastModifiedAt==c.CreatedAt && c.FiscalYearId==Year(f));
            var scoped=await Create(f,Request(f,f.BranchId));Ensure(scoped.ResourceId!=saved.ResourceId);
        });
        test("SYS10: invalid revision cannot consume numbers or partially edit series",f=>
        {
            var now=new DateTimeOffset(2026,11,2,14,0,0,TimeSpan.Zero);
            var series=new DocumentNumberSeries(f.CompanyId,f.BranchId,
                NumberingDocumentKind.Journal,Guid.NewGuid(),"JV-",7,"actor",now);
            var version=series.Version;
            try { series.Edit("BAD-",10,now.AddMinutes(-1)); throw new Exception("Edit succeeded"); }
            catch(BusinessException e) when(e.Code=="invalid_numbering_revision") {}
            try { series.Allocate(now.AddMinutes(-1)); throw new Exception("Allocation succeeded"); }
            catch(BusinessException e) when(e.Code=="invalid_numbering_revision") {}
            Ensure(series.Prefix=="JV-" && series.NextNumber==7 && series.Revision==1 &&
                series.Version==version && series.LastModifiedAt==now);
            series.Edit("NEW-",10,now.AddMinutes(1));
            version=series.Version;
            try { series.Allocate(now); throw new Exception("Stale allocation succeeded"); }
            catch(BusinessException e) when(e.Code=="invalid_numbering_revision") {}
            Ensure(series.Prefix=="NEW-" && series.NextNumber==10 && series.Revision==2 &&
                series.Version==version && series.LastModifiedAt==now.AddMinutes(1));
            var allocation=series.Allocate(now.AddMinutes(2));
            Ensure(allocation.Sequence==10 && allocation.Number=="NEW-000010" &&
                series.NextNumber==11 && series.Revision==3);
            return Task.CompletedTask;
        });
        test("SYS10 status atomicity: invalid timestamp cannot mutate activation or audit",f=>
        {
            var now=new DateTimeOffset(2026,11,2,12,0,0,TimeSpan.Zero);
            var card=new DocumentNumberSeries(f.CompanyId,f.BranchId,NumberingDocumentKind.Journal,
                Guid.NewGuid(),"JV-",7,"actor",now);
            var version=card.Version;
            try { card.SetActive(false,now.AddMinutes(-1)); throw new Exception("Expected invalid status timestamp"); }
            catch(BusinessException ex) when(ex.Code=="invalid_numbering_revision") { }
            Ensure(card.IsActive && card.Revision==1 && card.LastModifiedAt==now &&
                card.Version==version && card.NextNumber==7);
            card.SetActive(false,now.AddMinutes(1));
            Ensure(!card.IsActive && card.Revision==2 && card.LastModifiedAt==now.AddMinutes(1));
            version=card.Version;
            try { card.SetActive(true,now); throw new Exception("Expected stale status timestamp"); }
            catch(BusinessException ex) when(ex.Code=="invalid_numbering_revision") { }
            Ensure(!card.IsActive && card.Revision==2 && card.LastModifiedAt==now.AddMinutes(1) &&
                card.Version==version);
            return Task.CompletedTask;
        });
        test("SYS10 idempotency: concurrent create and reserve return one card/allocation and audit",async f=>
        {
            var key=Fixture.Key();var cards=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Create(f,key:key)));
            Ensure(cards.Distinct().Count()==1 && f.Store.Count<DocumentNumberSeries>()==1);
            key=Fixture.Key();var body=new ReserveDocumentNumberRequest(cards[0].Version,f.BranchId);
            var allocations=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>f.Service.Numbering.Reserve(f.CompanyId,cards[0].ResourceId,f.Owner,key,body,default)));
            Ensure(allocations.Distinct().Count()==1 && f.Store.Count<DocumentNumberAllocation>()==1 && allocations[0].Number=="CFG-000010");
            var c=await f.Service.Numbering.Get(f.CompanyId,cards[0].ResourceId,f.Owner,default);Ensure(c.NextNumber==11 && c.Revision==2);
            Ensure((await f.Service.Numbering.Changes(f.CompanyId,c.Id,f.Owner,0,20,default)).TotalCount==2);
        });
        test("SYS10 integration: configured journal numbers survive cancellation and never return to pool",async f=>
        {
            var series=await Create(f);var first=await f.Draft();Ensure(first.Number=="CFG-000010");
            await f.Action(first,"cancel",reason:"إلغاء");var second=await f.Draft();Ensure(second.Number=="CFG-000011");
            Ensure(f.Store.Count<NumberSequence>()==0);
            var usage=await f.Service.Numbering.Usage(f.CompanyId,series.ResourceId,f.Clerk,0,20,default);
            Ensure(usage.TotalCount==2 && usage.Items.Single(x=>x.DocumentId==first.ResourceId).DocumentState=="Cancelled" && usage.Items.All(x=>x.ReservedBy==f.Clerk.Subject));
            var c=await f.Service.Numbering.Get(f.CompanyId,series.ResourceId,f.Clerk,default);
            await Error("number_reuse_forbidden",()=>f.Service.Numbering.Edit(f.CompanyId,c.Id,f.Owner,Fixture.Key(),new(c.Version,10,"CFG-"),default));
        });
        test("SYS10 scope: branch override, global protection and inactive branch series cannot fall back",async f=>
        {
            var global=await Create(f);var scoped=await Create(f,Request(f,f.BranchId) with {Prefix="BR-"});
            await f.Service.Membership.GrantMembership(f.CompanyId,f.Owner,Fixture.Key(),new(f.Limited.Subject,Permissions.All,false,new[]{f.BranchId},f.Store.All<Membership>().Single(x=>x.Subject==f.Limited.Subject).Version),default);
            await Error("all_branches_required",()=>f.Service.Numbering.Get(f.CompanyId,global.ResourceId,f.Limited,default));
            Ensure((await f.Service.Numbering.Search(f.CompanyId,f.Limited,null,null,null,0,20,default)).Items.Single().Id==scoped.ResourceId);
            Ensure((await f.Draft()).Number=="BR-000010" && (await f.Draft(f.Request(branch:f.OtherBranchId))).Number=="CFG-000010");
            var c=await f.Service.Numbering.Get(f.CompanyId,scoped.ResourceId,f.Owner,default);
            var disabled=await f.Service.Numbering.Status(f.CompanyId,c.Id,f.Owner,Fixture.Key(),new(c.Version,false,"تعطيل"),default);
            var count=f.Store.Count<DocumentNumberAllocation>();await Error("numbering_inactive",()=>f.Draft());Ensure(f.Store.Count<DocumentNumberAllocation>()==count);
            await f.Service.Numbering.Status(f.CompanyId,c.Id,f.Owner,Fixture.Key(),new(disabled.Version,true,"تفعيل"),default);
            Ensure((await f.Draft()).Number=="BR-000011");
        });
        test("SYS10 editing: advance next/prefix, stale conflict, replay and old allocation snapshots",async f=>
        {
            var created=await Create(f);await f.Draft();var c=await f.Service.Numbering.Get(f.CompanyId,created.ResourceId,f.Owner,default);
            var key=Fixture.Key();var body=new EditNumberSeriesRequest(c.Version,30,"NEW-");
            var edited=await f.Service.Numbering.Edit(f.CompanyId,c.Id,f.Owner,key,body,default);
            Ensure(edited==await f.Service.Numbering.Edit(f.CompanyId,c.Id,f.Owner,key,body,default));
            await Error("version_conflict",()=>f.Service.Numbering.Edit(f.CompanyId,c.Id,f.Owner,Fixture.Key(),body,default));
            Ensure((await f.Draft()).Number=="NEW-000030");
            Ensure(f.Store.All<DocumentNumberAllocation>().Any(x=>x.Number=="CFG-000010" && x.SeriesVersion==created.Version));
            Ensure((await f.Service.Numbering.Search(f.CompanyId,f.Owner,null,true,"NEW-",0,1,default)).TotalCount==1);
        });
        test("SYS10 rollback: failed document validation or commit restores counter reservation audit and receipt",async f=>
        {
            var created=await Create(f);var bad=f.Request() with {Lines=f.Request().Lines.Select(x=>x with {AccountId=Guid.NewGuid()}).ToArray()};
            await Error("reference_not_found",()=>f.Draft(bad));
            var c=await f.Service.Numbering.Get(f.CompanyId,created.ResourceId,f.Owner,default);Ensure(c.NextNumber==10 && c.Revision==1 && f.Store.Count<DocumentNumberAllocation>()==0);
            var key=Fixture.Key();f.Store.FailBeforeCommit=true;
            try{await f.Draft(key:key);throw new Exception("Expected persistence failure");}catch(IOException){}
            c=await f.Service.Numbering.Get(f.CompanyId,created.ResourceId,f.Owner,default);Ensure(c.NextNumber==10 && c.Revision==1 && f.Store.Count<DocumentNumberAllocation>()==0 && !f.Store.All<IdempotencyRecord>().Any(x=>x.Key==key));
            Ensure((await f.Draft(key:key)).Number=="CFG-000010");
        });
        test("SYS10 constraints: duplicate scope and cross-series/legacy number collisions reject without consuming number",async f=>
        {
            await Create(f);await Error("duplicate_numbering_scope",()=>Create(f));await f.Draft();
            await Error("document_number_used",()=>Create(f,Request(f,f.OtherBranchId)));
            var legacy=await f.Draft(f.Request(branch:f.OtherBranchId));Ensure(legacy.Number=="CFG-000011");
            var c=f.Store.All<DocumentNumberSeries>().Single();Ensure(c.NextNumber==12);
        });
        test("SYS10 reference/security validation: foreign year, invalid next and unconfigured tax profile are rejected",async f=>
        {
            await Error("reference_not_found",()=>Create(f,Request(f) with {FiscalYearId=Guid.NewGuid()}));
            await Error("invalid_next_number",()=>Create(f,Request(f) with {NextNumber=0}));
            await Error("tax_numbering_profile_required",()=>Create(f,Request(f) with {DocumentKind=NumberingDocumentKind.TaxInvoice}));
            await Error("permission_denied",()=>f.Service.Numbering.Create(f.CompanyId,f.Clerk,Fixture.Key(),Request(f),default));
            var scoped=await Create(f,Request(f,f.OtherBranchId));
            await Error("branch_access_denied",()=>f.Service.Numbering.Get(f.CompanyId,scoped.ResourceId,f.Limited,default));
        });
        test("SYS10 GL06 dependency: configured textual ranges stay within one series and do not mix legacy counters",async f=>
        {
            var old=await f.Draft();await Create(f,Request(f) with {NextNumber=1});
            var a=await f.Draft();var b=await f.Draft();
            foreach(var j in new[]{old,a,b})await f.Action(j,"submit");
            var period=(await f.Journal(a.ResourceId)).FiscalPeriodId;
            var selected=await f.Service.Journal.ProcessingQueue(f.CompanyId,f.Approver,period,null,null,null,null,null,"approve",0,20,default,a.Number+".."+b.Number);
            Ensure(selected.TotalCount==2 && selected.Items.All(x=>x.Journal.Id!=old.ResourceId));
            var legacy=await f.Service.Journal.ProcessingQueue(f.CompanyId,f.Approver,period,null,null,null,null,null,"approve",0,20,default,old.Number);
            Ensure(legacy.TotalCount==1 && legacy.Items.Single().Journal.Id==old.ResourceId);
            await Error("invalid_journal_number_range",()=>f.Service.Journal.ProcessingQueue(f.CompanyId,f.Approver,period,null,null,null,null,null,"approve",0,20,default,old.Number+".."+a.Number));
        });
        test("SYS10 traceability: renumbering a draft into a new year retains its immutable old allocation link",async f=>
        {
            var oldSeries=await Create(f);var draft=await f.Draft();
            var year=await f.Service.FiscalCalendar.CreateFiscalYear(f.CompanyId,f.Owner,Fixture.Key(),new("2027",new(2027,1,1),new(2027,12,31)),default);
            await Create(f,new(NumberingDocumentKind.Journal,year.ResourceId,1,"YR27-"));
            var changed=await f.Service.Journal.EditJournal(f.CompanyId,draft.ResourceId,f.Clerk,Fixture.Key(),new(draft.Version,"نقل السنة",f.Request().Lines,new(f.BranchId,new(2027,1,1),new(2027,1,1),null)),default);
            Ensure(changed.Number=="YR27-000001");
            var usage=await f.Service.Numbering.Usage(f.CompanyId,oldSeries.ResourceId,f.Clerk,0,20,default);
            Ensure(usage.Items.Single().DocumentId==draft.ResourceId && usage.Items.Single().Number=="CFG-000010");
            Ensure(f.Store.All<DocumentNumberAllocation>().Count(x=>x.DocumentId==draft.ResourceId)==2);
        });
        test("SYS10 inventory dependency: configured opening number is linked without granting journal/configuration privileges",async f=>
        {
            var request=await InventoryOpeningTests.Setup(f);
            var series=await Create(f,new(NumberingDocumentKind.InventoryOpening,Year(f),77,"STOCK-"));
            var opening=await InventoryOpeningTests.Create(f,request);
            var usage=await f.Service.Numbering.Usage(f.CompanyId,series.ResourceId,f.Owner,0,20,default);
            Ensure(opening.Number=="STOCK-000077" && usage.Items.Single().DocumentId==opening.ResourceId && usage.Items.Single().ReservedBy==InventoryOpeningTests.Writer.Subject);
            Ensure(!f.Store.All<Membership>().Single(x=>x.Subject==InventoryOpeningTests.Writer.Subject).Has(Permissions.Configure));
        });
        test("SYS10 allocation history is immutable in persistence",async f=>
        {
            await Create(f);await f.Draft();var a=f.Store.All<DocumentNumberAllocation>().Single();
            using var db=new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=localhost;Database=GuardOnly;Integrated Security=true;TrustServerCertificate=true").Options);
            db.Attach(a);a.Touch();await Error("number_allocation_immutable",()=>db.SaveChangesAsync());
        });
    }
    private static void Ensure(bool ok){if(!ok)throw new Exception("SYS10 assertion failed");}
    private static async Task Error(string code,Func<Task> action){try{await action();}catch(BusinessException e)when(e.Code==code){return;}throw new Exception("Expected "+code);}
}
