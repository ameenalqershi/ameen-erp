using System.Text.Json;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class FinancialMappingService : ApplicationService
{
    public FinancialMappingService(IErpStore store,ITransactionRunner tx,IClock clock):base(store,tx,clock) { }
    // The source account catalog is company-wide (same boundary as GL01), even for a branch-specific map.
    private async Task<FinancialMapping> Card(Guid cid,Guid id,Actor actor,string permission,CancellationToken ct)
    {await AuthorizeGlobal(cid,actor,permission,ct);return await Required<FinancialMapping>(cid,id,ct);}
    private async Task ValidateScope(Guid cid,Guid? branch,CancellationToken ct)
    {if(branch is Guid id)Check.That((await Required<Branch>(cid,id,ct)).IsActive,"branch_inactive","الفرع معطل.");}
    private async Task Unique(FinancialMapping map,CancellationToken ct)
    {Check.That(!(await store.Find<FinancialMapping>(map.CompanyId,x=>x.Id!=map.Id && x.BranchId==map.BranchId && x.Name==map.Name && x.StatementKind==map.StatementKind && x.VersionCode==map.VersionCode,ct)).Any(),"duplicate_mapping_version","اسم النموذج ونوعه وإصداره موجودة في النطاق نفسه.",ErrorKind.Conflict);}
    private static List<FinancialMappingLine> Lines(FinancialMapping map,FinancialMappingLineRequest[]? requests)
    {
        Check.That(requests is {Length:>=1 and <=500},"invalid_mapping_lines","النموذج يتكون من 1 إلى 500 بند.");var rows=new List<FinancialMappingLine>();
        foreach(var r in requests!)
        {Check.That(r is not null,"invalid_mapping_line","بند فارغ.");rows.Add(new(map.CompanyId,map.Id,rows.Count+1,r!.Code,r.ParentCode,r.Title,r.AccountId,r.DisplaySign,r.Formula));}
        Check.That(rows.Select(x=>x.Code).Distinct(StringComparer.Ordinal).Count()==rows.Count,"duplicate_mapping_code","رمز بند مكرر.");
        var byCode=rows.ToDictionary(x=>x.Code,StringComparer.Ordinal);
        foreach(var line in rows)
        {
            if(line.ParentCode is null)continue;
            Check.That(byCode.TryGetValue(line.ParentCode,out var parent),"mapping_parent_not_found","بند أب غير موجود.");line.LinkParent(parent!.Id);
        }
        return rows;
    }
    private async Task ValidateDefinition(FinancialMapping map,CancellationToken ct)
    {
        await ValidateScope(map.CompanyId,map.BranchId,ct);await Unique(map,ct);
        FinancialMappingCompiler.Evaluate(map,await store.Find<Account>(map.CompanyId,x=>true,ct),new Dictionary<Guid,decimal>(),map.EffectiveFrom,true);
    }
    public Task<CommandReceipt> Create(Guid cid,Actor actor,string key,CreateFinancialMappingRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"financial-mapping.create",r,t=>AuthorizeGlobal(cid,actor,Permissions.Configure,t),async t=>
        {
            var m=new FinancialMapping(cid,r.BranchId,r.Name,r.StatementKind,r.VersionCode,r.EffectiveFrom,actor.Subject,clock.UtcNow);
            m.SetLines(Lines(m,r.Lines));await ValidateDefinition(m,t);store.Add(m);Audit(cid,actor,"financial-mapping.created",m,m);return Receipt(m,"Active");
        },ct);
    public Task<CommandReceipt> Edit(Guid cid,Guid id,Actor actor,string key,EditFinancialMappingRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"financial-mapping.edit:"+id,r,t=>Card(cid,id,actor,Permissions.Configure,t),async t=>
        {
            var m=await Required<FinancialMapping>(cid,id,t);Check.Version(m,r.ExpectedVersion);
            var before=JsonSerializer.Serialize(m,JsonDefaults.Options);var old=m.Lines.ToArray();
            m.Configure(r.Name,r.StatementKind,r.VersionCode,r.EffectiveFrom);m.SetLines(Lines(m,r.Lines));await ValidateDefinition(m,t);
            store.RemoveFinancialMappingLines(old);m.Advance(clock.UtcNow);Audit(cid,actor,"financial-mapping.edited",m,new{before,after=m});return Receipt(m,m.IsActive?"Active":"Inactive");
        },ct);
    public Task<CommandReceipt> Status(Guid cid,Guid id,Actor actor,string key,SetFinancialMappingStatusRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"financial-mapping.status:"+id,r,t=>Card(cid,id,actor,Permissions.Configure,t),async t=>
        {var m=await Required<FinancialMapping>(cid,id,t);Check.Version(m,r.ExpectedVersion);var reason=Check.Text(r.Reason,"السبب",500);m.SetActive(r.IsActive,clock.UtcNow);Audit(cid,actor,"financial-mapping.status-changed",m,new{m.IsActive,m.Revision,m.LastModifiedAt,reason});return Receipt(m,m.IsActive?"Active":"Inactive");},ct);
    public Task<CommandReceipt> Review(Guid cid,Guid id,Actor actor,string key,ReviewFinancialMappingRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"financial-mapping.review:"+id,r,t=>Card(cid,id,actor,Permissions.Read,t),async t=>
        {
            var m=await Required<FinancialMapping>(cid,id,t);Check.Version(m,r.ExpectedVersion);
            Check.That(m.IsActive,"mapping_inactive","النموذج معطل؛ المراجعات السابقة تظل متاحة.",ErrorKind.Conflict);
            Check.That(r.AsOf>=m.EffectiveFrom && r.AsOf!=DateOnly.MinValue,"mapping_not_effective","تاريخ المراجعة يسبق سريان النموذج.");
            var journals=await store.Find<JournalEntry>(cid,x=>(m.BranchId==null || x.BranchId==m.BranchId) && x.AccountingDate<=r.AsOf && (x.State==JournalState.Posted || x.State==JournalState.Reversed),t);
            Dictionary<Guid,decimal> balances;
            try{balances=journals.SelectMany(x=>x.Lines).GroupBy(x=>x.AccountId).ToDictionary(x=>x.Key,x=>x.Sum(a=>a.FunctionalDebit-a.FunctionalCredit));}
            catch(OverflowException){throw new BusinessException("mapping_amount_overflow","مجموع الأستاذ يتجاوز الدقة المدعومة.");}
            var result=FinancialMappingCompiler.Evaluate(m,await store.Find<Account>(cid,x=>true,t),balances,r.AsOf,false,(await Company(cid,t)).FunctionalCurrency);
            var review=new FinancialMappingReview(m,r.AsOf,actor.Subject,clock.UtcNow,result.Ready,JsonSerializer.Serialize(result,JsonDefaults.Options));
            store.Add(review);Audit(cid,actor,"financial-mapping.reviewed",m,new{ReviewId=review.Id,review.MappingVersion,review.AsOf,result.Ready,result.RequiredAccountCount,result.ClassifiedAccountCount,result.Issues});
            return Receipt(review,result.Ready?"Ready":"NeedsReview");
        },ct);
}
