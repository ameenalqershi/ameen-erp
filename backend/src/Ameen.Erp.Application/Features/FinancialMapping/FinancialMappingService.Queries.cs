using System.Text.Json;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class FinancialMappingService
{
    public Task<FinancialMapping> Get(Guid cid,Guid id,Actor actor,CancellationToken ct)=>Card(cid,id,actor,Permissions.Read,ct);
    public async Task<Page<FinancialMapping>> Search(Guid cid,Actor actor,Guid? branch,FinancialStatementKind? kind,bool? active,string? search,int offset,int limit,CancellationToken ct)
    {
        Paging(offset,limit);await AuthorizeGlobal(cid,actor,Permissions.Read,ct);
        Check.That(kind is null || Enum.IsDefined(kind.Value),"invalid_statement_kind","نوع قائمة غير صالح.");
        Check.That(search is null || search.Length<=100,"search_too_long","عبارة البحث طويلة.");search=search?.Trim();
        if(branch is Guid bid)await Required<Branch>(cid,bid,ct);
        return await store.ReadPage<FinancialMapping>(cid,x=>(branch==null || x.BranchId==branch) && (kind==null || x.StatementKind==kind) && (active==null || x.IsActive==active) && (search==null || x.Name.Contains(search) || x.VersionCode.Contains(search)),
            q=>q.OrderBy(x=>x.Name).ThenByDescending(x=>x.EffectiveFrom).ThenBy(x=>x.Id),offset,limit,ct);
    }
    public async Task<Page<AuditEvent>> Changes(Guid cid,Guid id,Actor actor,int offset,int limit,CancellationToken ct)
    {Paging(offset,limit);await Card(cid,id,actor,Permissions.Read,ct);await AuthorizeGlobal(cid,actor,Permissions.Audit,ct);return await store.ReadPage<AuditEvent>(cid,x=>x.ResourceId==id && x.Action.StartsWith("financial-mapping."),q=>q.OrderByDescending(x=>x.OccurredAt).ThenBy(x=>x.Id),offset,limit,ct);}
    public async Task<Page<FinancialMappingReviewSummary>> Usage(Guid cid,Guid id,Actor actor,int offset,int limit,CancellationToken ct)
    {
        Paging(offset,limit);await Card(cid,id,actor,Permissions.Read,ct);
        var page=await store.ReadPage<FinancialMappingReview>(cid,x=>x.MappingId==id,q=>q.OrderByDescending(x=>x.ReviewedAt).ThenBy(x=>x.Id),offset,limit,ct);
        return new(page.Items.Select(x=>new FinancialMappingReviewSummary(x.Id,x.MappingVersion,x.AsOf,x.ReviewedBy,x.ReviewedAt,x.Ready)).ToArray(),page.TotalCount,offset,limit);
    }
    public async Task<FinancialMappingReviewDetail> ReviewDetail(Guid cid,Guid id,Actor actor,CancellationToken ct)
    {
        await AuthorizeGlobal(cid,actor,Permissions.Read,ct);var r=await Required<FinancialMappingReview>(cid,id,ct);await Card(cid,r.MappingId,actor,Permissions.Read,ct);
        return new(r.Id,r.MappingId,r.MappingVersion,r.AsOf,r.ReviewedBy,r.ReviewedAt,JsonSerializer.Deserialize<FinancialMappingEvaluation>(r.SnapshotJson,JsonDefaults.Options)!);
    }
}
