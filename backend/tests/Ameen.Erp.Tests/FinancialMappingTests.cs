using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class FinancialMappingTests
{
    public static FinancialMappingLineRequest[] Lines(Fixture f)=>new FinancialMappingLineRequest[]{
        new("ROOT","عنوان",FinancialDisplaySign.Normal),
        new("CASH","النقدية",FinancialDisplaySign.Normal,f.GroupId,"ROOT"),
        new("EQUITY","الملكية",FinancialDisplaySign.Reverse,f.EquityId,"ROOT"),
        new("NET","الصافي",FinancialDisplaySign.Normal,null,"ROOT","[CASH] - ([EQUITY])")};
    public static CreateFinancialMappingRequest Request(Fixture f)=>new("المركز المالي",FinancialStatementKind.FinancialPosition,"V1",new(2026,1,1),Lines(f));
    private static Task<CommandReceipt> Create(Fixture f,CreateFinancialMappingRequest? r=null,string? key=null)=>f.Service.FinancialMapping.Create(f.CompanyId,f.Owner,key??Fixture.Key(),r??Request(f),default);
    public static EditFinancialMappingRequest Edit(FinancialMapping m,FinancialMappingLineRequest[] lines)=>new(m.Version,m.Name,m.StatementKind,m.VersionCode,m.EffectiveFrom,lines);
    private static async Task<FinancialMappingReviewDetail> Review(Fixture f,CommandReceipt m,DateOnly? date=null)
    {var r=await f.Service.FinancialMapping.Review(f.CompanyId,m.ResourceId,f.Clerk,Fixture.Key(),new(m.Version,date??Fixture.Date),default);return await f.Service.FinancialMapping.ReviewDetail(f.CompanyId,r.ResourceId,f.Clerk,default);}
    public static void Register(Action<string,Func<Fixture,Task>> test)
    {
        test("GL13 fields: header, optional branch, lines and server audit preserve source types",async f=>
        {
            var r=await Create(f);var m=await f.Service.FinancialMapping.Get(f.CompanyId,r.ResourceId,f.Owner,default);
            Ensure(m.CompanyId==f.CompanyId && m.BranchId is null && m.IsActive && m.Name==Request(f).Name && m.StatementKind==FinancialStatementKind.FinancialPosition && m.VersionCode=="V1" && m.EffectiveFrom==new DateOnly(2026,1,1));
            Ensure(m.CreatedBy==f.Owner.Subject && m.Revision==1 && m.LastModifiedAt==m.CreatedAt && m.Version!=Guid.Empty);
            Ensure(m.Lines[1].ParentCode=="ROOT" && m.Lines[1].ParentLineId==m.Lines[0].Id && m.Lines[1].AccountId==f.GroupId && m.Lines[3].Formula=="[CASH] - ([EQUITY])");
            var scoped=await Create(f,Request(f) with{BranchId=f.BranchId});Ensure(scoped.ResourceId!=r.ResourceId);
        });
        test("GL13 acceptance: groups, signs, nested formulas and posted ledger produce exact auditable rows",async f=>
        {
            await f.Post();await f.Draft(f.Request(900,900));var m=await Create(f);var detail=await Review(f,m);var e=detail.Evaluation;
            Ensure(e.Ready && e.Issues.Length==0 && e.FunctionalCurrency=="SAR" && e.RequiredAccountCount==2 && e.ClassifiedAccountCount==2);
            Ensure(e.Rows.Single(x=>x.Code=="CASH").Amount==100 && e.Rows.Single(x=>x.Code=="EQUITY").Amount==100 && e.Rows.Single(x=>x.Code=="NET").Amount==0 && e.Rows.Single(x=>x.Code=="ROOT").Amount==0);
            Ensure(e.Rows.Single(x=>x.Code=="NET").AccountIds.ToHashSet().SetEquals(new[]{f.CashId,f.EquityId}) && detail.MappingVersion==m.Version && detail.ReviewedBy==f.Clerk.Subject);
        });
        test("GL13 review: as-of, branch scope and posted reversal keep historical balances",async f=>
        {
            var original=await f.Post();await f.Post(await f.Draft(f.Request(25,25,f.OtherBranchId)));
            var m=await Create(f,Request(f) with{BranchId=f.BranchId});Ensure((await Review(f,m)).Evaluation.Rows.Single(x=>x.Code=="CASH").Amount==100);
            Ensure((await Review(f,m,new(2026,10,19))).Evaluation.Rows.All(x=>x.Amount==0));
            var inverse=await f.Service.Journal.ReverseJournal(f.CompanyId,original.ResourceId,f.Clerk,Fixture.Key(),new(original.Version,Fixture.Date,"عكس"),default);await f.Post(inverse);
            Ensure((await Review(f,m)).Evaluation.Rows.All(x=>x.Amount==0));
        });
        test("GL13 control: unclassified active or historical inactive account is an explicit review error",async f=>
        {
            await f.Post();var m=await Create(f,Request(f) with{Lines=new[]{Lines(f)[1] with{ParentCode=null}}});var review=await Review(f,m);
            Ensure(!review.Evaluation.Ready && review.Evaluation.Issues.Single().AccountId==f.EquityId && review.Evaluation.Issues.Single().Code=="account_unclassified");
            f.Store.All<Account>().Single(x=>x.Id==f.EquityId).SetActive(false);
            Ensure((await Review(f,m)).Evaluation.Issues.Single().AccountId==f.EquityId);
        });
        test("GL13 control: direct, group-child and nested formula double counting rejected atomically",async f=>
        {
            var lines=Lines(f);await Error("duplicate_mapping_account",()=>Create(f,Request(f) with{Lines=lines.Append(new("CASH2","مكرر",FinancialDisplaySign.Normal,f.CashId)).ToArray()}));
            await Error("duplicate_mapping_aggregation",()=>Create(f,Request(f) with{Lines=lines.Select(x=>x.Code=="NET"?x with{Formula="[CASH]+[CASH]"}:x).ToArray()}));
            await Error("duplicate_mapping_aggregation",()=>Create(f,Request(f) with{Lines=lines.Append(new("DOUBLE","تكرار غير مباشر",FinancialDisplaySign.Normal,null,null,"[NET]+[CASH]")).ToArray()}));
            Ensure(f.Store.Count<FinancialMapping>()==0);
        });
        test("GL13 control: parent/formula cycles, self-reference and missing references reject",async f=>
        {
            await Error("mapping_parent_cycle",()=>Create(f,Request(f) with{Lines=Lines(f).Select(x=>x.Code=="ROOT"?x with{ParentCode="CASH"}:x).ToArray()}));
            await Error("mapping_formula_cycle",()=>Create(f,Request(f) with{Lines=new FinancialMappingLineRequest[]{new("A","أ",FinancialDisplaySign.Normal,null,null,"[B]"),new("B","ب",FinancialDisplaySign.Normal,null,null,"[A]")}}));
            await Error("mapping_formula_cycle",()=>Create(f,Request(f) with{Lines=new[]{new FinancialMappingLineRequest("A","أ",FinancialDisplaySign.Normal,null,null,"[A]")}}));
            await Error("mapping_reference_not_found",()=>Create(f,Request(f) with{Lines=new[]{new FinancialMappingLineRequest("A","أ",FinancialDisplaySign.Normal,null,null,"[MISSING]")}}));
            await Error("mapping_parent_not_found",()=>Create(f,Request(f) with{Lines=new[]{Lines(f)[1]}}));
        });
        test("GL13 formulas: safe grammar rejects arbitrary expressions and excessive parentheses",async f=>
        {
            foreach(var formula in new[]{"1+2","[CASH]*2","[CASH] / [EQUITY]","[CASH","[CASH])","[CASH]+","System.IO.File()"})
                await Error("invalid_mapping_formula",()=>Create(f,Request(f) with{Lines=Lines(f).Select(x=>x.Code=="NET"?x with{Formula=formula}:x).ToArray()}));
            await Error("mapping_formula_depth",()=>Create(f,Request(f) with{Lines=Lines(f).Select(x=>x.Code=="NET"?x with{Formula=new string('(',33)+"[CASH]"+new string(')',33)}:x).ToArray()}));
            var m=await Create(f,Request(f) with{Lines=Lines(f).Select(x=>x.Code=="NET"?x with{Formula="-(-[cash]) - +[equity]"}:x).ToArray()});Ensure((await Review(f,m)).Evaluation.Ready);
        });
        test("GL13 writes: concurrent replay, version conflicts, line replacement and numeric revision",async f=>
        {
            var key=Fixture.Key();var rs=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Create(f,key:key)));Ensure(rs.Distinct().Count()==1);
            var m=await f.Service.FinancialMapping.Get(f.CompanyId,rs[0].ResourceId,f.Owner,default);var body=Edit(m,Lines(f).Reverse().ToArray()) with{Name="معدل"};key=Fixture.Key();
            var edited=await f.Service.FinancialMapping.Edit(f.CompanyId,m.Id,f.Owner,key,body,default);
            Ensure(edited==await f.Service.FinancialMapping.Edit(f.CompanyId,m.Id,f.Owner,key,body,default));
            await Error("version_conflict",()=>f.Service.FinancialMapping.Edit(f.CompanyId,m.Id,f.Owner,Fixture.Key(),body,default));
            m=await f.Service.FinancialMapping.Get(f.CompanyId,m.Id,f.Owner,default);Ensure(m.Revision==2 && m.CreatedBy==f.Owner.Subject && m.Lines[0].Code=="NET" && m.LastModifiedAt>=m.CreatedAt);
            await Error("duplicate_mapping_version",()=>Create(f,Request(f) with{Name="معدل"}));
            Ensure((await f.Service.FinancialMapping.Changes(f.CompanyId,m.Id,f.Owner,0,20,default)).TotalCount==2);
        });
        test("GL13 history: immutable reviews retain original definition and amounts after map edit/disable",async f=>
        {
            await f.Post();var m=await Create(f);var review=await Review(f,m);var card=await f.Service.FinancialMapping.Get(f.CompanyId,m.ResourceId,f.Owner,default);
            var edited=await f.Service.FinancialMapping.Edit(f.CompanyId,card.Id,f.Owner,Fixture.Key(),Edit(card,Lines(f)) with{Name="اسم جديد",VersionCode="V2"},default);
            await f.Service.FinancialMapping.Status(f.CompanyId,card.Id,f.Owner,Fixture.Key(),new(edited.Version,false,"تعطيل"),default);
            var old=await f.Service.FinancialMapping.ReviewDetail(f.CompanyId,review.Id,f.Clerk,default);Ensure(old.MappingVersion==m.Version && old.Evaluation.Name==Request(f).Name && old.Evaluation.VersionCode=="V1" && old.Evaluation.Rows.Single(x=>x.Code=="CASH").Amount==100);
            Ensure((await f.Service.FinancialMapping.Usage(f.CompanyId,card.Id,f.Clerk,0,10,default)).Items.Single().Id==review.Id);
            card=await f.Service.FinancialMapping.Get(f.CompanyId,card.Id,f.Owner,default);
            await Error("mapping_inactive",()=>f.Service.FinancialMapping.Review(f.CompanyId,card.Id,f.Clerk,Fixture.Key(),new(card.Version,Fixture.Date),default));
        });
        test("GL13 atomicity: failed edit/review rolls back definition, revision, snapshots, audits and receipt",async f=>
        {
            var m=await Create(f);var card=await f.Service.FinancialMapping.Get(f.CompanyId,m.ResourceId,f.Owner,default);var key=Fixture.Key();var body=Edit(card,Lines(f)) with{Name="لا يتسرب"};
            f.Store.FailBeforeCommit=true;try{await f.Service.FinancialMapping.Edit(f.CompanyId,card.Id,f.Owner,key,body,default);throw new Exception("Expected failure");}catch(IOException){}
            card=await f.Service.FinancialMapping.Get(f.CompanyId,card.Id,f.Owner,default);Ensure(card.Version==m.Version && card.Revision==1 && card.Name==Request(f).Name && !f.Store.All<IdempotencyRecord>().Any(x=>x.Key==key));
            var edited=await f.Service.FinancialMapping.Edit(f.CompanyId,card.Id,f.Owner,key,body,default);key=Fixture.Key();f.Store.FailBeforeCommit=true;
            try{await f.Service.FinancialMapping.Review(f.CompanyId,card.Id,f.Clerk,key,new(edited.Version,Fixture.Date),default);throw new Exception("Expected failure");}catch(IOException){}
            Ensure(f.Store.Count<FinancialMappingReview>()==0 && !f.Store.All<IdempotencyRecord>().Any(x=>x.Key==key));
            var review=await f.Service.FinancialMapping.Review(f.CompanyId,card.Id,f.Clerk,key,new(edited.Version,Fixture.Date),default);Ensure(review.Status=="Ready");
        });
        test("GL13 authorization: global catalog requires all branches, configure writes and audit permission",async f=>
        {
            var m=await Create(f);await Error("permission_denied",()=>f.Service.FinancialMapping.Create(f.CompanyId,f.Clerk,Fixture.Key(),Request(f),default));
            await Error("all_branches_required",()=>f.Service.FinancialMapping.Get(f.CompanyId,m.ResourceId,f.Limited,default));
            await Error("permission_denied",()=>f.Service.FinancialMapping.Changes(f.CompanyId,m.ResourceId,f.Clerk,0,20,default));
            await Error("reference_not_found",()=>f.Service.FinancialMapping.Get(f.CompanyId,Guid.NewGuid(),f.Owner,default));
            await Error("mapping_not_effective",()=>f.Service.FinancialMapping.Review(f.CompanyId,m.ResourceId,f.Clerk,Fixture.Key(),new(m.Version,new(2025,12,31)),default));
        });
        test("GL13 references: inactive/foreign accounts, invalid signs/dates and ambiguous lines reject",async f=>
        {
            await Error("reference_not_found",()=>Create(f,Request(f) with{BranchId=Guid.NewGuid()}));
            await Error("reference_not_found",()=>Create(f,Request(f) with{Lines=new[]{new FinancialMappingLineRequest("X","أ",FinancialDisplaySign.Normal,Guid.NewGuid())}}));
            f.Store.All<Account>().Single(x=>x.Id==f.CashId).SetActive(false);
            await Error("mapping_account_inactive",()=>Create(f,Request(f) with{Lines=new[]{new FinancialMappingLineRequest("X","أ",FinancialDisplaySign.Normal,f.CashId)}}));
            await Error("invalid_command_payload",()=>Create(f,Request(f) with{Lines=new[]{new FinancialMappingLineRequest("X","أ",(FinancialDisplaySign)0)}}));
            await Error("mapping_date_required",()=>Create(f,Request(f) with{EffectiveFrom=default}));
            await Error("ambiguous_mapping_line",()=>Create(f,Request(f) with{Lines=new[]{new FinancialMappingLineRequest("X","أ",FinancialDisplaySign.Normal,f.EquityId,null,"[X]")}}));
        });
        test("GL13 query: search/type/branch/status and usage pagination expose only actual snapshots",async f=>
        {
            var a=await Create(f);await Create(f,Request(f) with{BranchId=f.BranchId,StatementKind=FinancialStatementKind.CashFlow});await Review(f,a);await Review(f,a);
            var page=await f.Service.FinancialMapping.Search(f.CompanyId,f.Owner,f.BranchId,FinancialStatementKind.CashFlow,true,"V1",0,1,default);Ensure(page.TotalCount==1 && page.Items.Single().BranchId==f.BranchId);
            Ensure((await f.Service.FinancialMapping.Usage(f.CompanyId,a.ResourceId,f.Clerk,1,1,default)).TotalCount==2);
            Ensure((await f.Service.FinancialMapping.Get(f.CompanyId,a.ResourceId,f.Owner,default)).Revision==1);
        });
        test("GL13 dependency: mapped group descendants lock GL01 structure and appear in account usage",async f=>
        {
            var map=await Create(f);var account=f.Store.All<Account>().Single(x=>x.Id==f.CashId);
            var usage=await f.Service.Account.GetAccountUsage(f.CompanyId,account.Id,f.Owner,0,20,default);
            Ensure(usage.References.Items.Any(x=>x.Kind=="FinancialMapping" && x.ResourceId==map.ResourceId));
            await Error("account_structure_in_use",()=>f.Service.Account.EditAccount(f.CompanyId,account.Id,f.Owner,Fixture.Key(),AccountTests.Edit(account) with{Code="NEWCODE"},default));
        });
        test("GL13 wire contract: all mandatory fields required and client audit spoofing rejected",f=>
        {
            var json=JsonSerializer.Serialize(Request(f),JsonDefaults.Options);using var doc=JsonDocument.Parse(json);
            foreach(var field in new[]{"name","statementKind","versionCode","effectiveFrom","lines"})
            {var obj=doc.RootElement.EnumerateObject().Where(x=>x.Name!=field).ToDictionary(x=>x.Name,x=>x.Value);try{JsonSerializer.Deserialize<CreateFinancialMappingRequest>(JsonSerializer.Serialize(obj),JsonDefaults.Options);throw new Exception("Missing field accepted: "+field);}catch(JsonException){}}
            try{JsonSerializer.Deserialize<CreateFinancialMappingRequest>(json[..^1]+",\"createdBy\":\"forged\"}",JsonDefaults.Options);throw new Exception("Audit spoof accepted");}catch(JsonException){}
            return Task.CompletedTask;
        });
        test("GL13 persistence: frozen review and detached line mutation are rejected before SQL",async f=>
        {
            var m=await Create(f);var detail=await Review(f,m);var review=f.Store.All<FinancialMappingReview>().Single();
            using var db=new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=localhost;Database=Guard;Integrated Security=true;TrustServerCertificate=true").Options);
            db.Attach(review);review.Touch();await Error("mapping_review_immutable",()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var line=f.Store.All<FinancialMapping>().Single().Lines[0];db.Attach(line);line.Touch();await Error("mapping_lines_owner_required",()=>db.SaveChangesAsync());
        });
    }
    private static void Ensure(bool ok){if(!ok)throw new Exception("GL13 assertion failed");}
    private static async Task Error(string code,Func<Task> action){try{await action();}catch(BusinessException e)when(e.Code==code){return;}throw new Exception("Expected "+code);}
}
