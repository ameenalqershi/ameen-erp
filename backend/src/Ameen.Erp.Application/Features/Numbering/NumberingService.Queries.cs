using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class NumberingService
{
    public Task<DocumentNumberSeries> Get(Guid cid,Guid id,Actor actor,CancellationToken ct)=>Card(cid,id,actor,Permissions.Read,ct);
    public async Task<Page<DocumentNumberSeries>> Search(Guid cid,Actor actor,Guid? branch,bool? active,string? search,int offset,int limit,CancellationToken ct)
    {
        Paging(offset,limit);var m=await Authorize(cid,actor,Permissions.Read,branch,ct);var branches=await AllowedBranches(cid,m,branch,ct);
        Check.That(search is null || search.Length<=100,"search_too_long","عبارة البحث طويلة.");search=search?.Trim();
        return await store.ReadPage<DocumentNumberSeries>(cid,x=>(x.BranchId==null?m.AllBranches && branch==null:branches.Contains(x.BranchId.Value)) && (active==null || x.IsActive==active) && (search==null || x.Prefix!=null && x.Prefix.Contains(search)),q=>q.OrderBy(x=>x.DocumentKind).ThenBy(x=>x.FiscalYearId).ThenBy(x=>x.Id),offset,limit,ct);
    }
    public async Task<Page<AuditEvent>> Changes(Guid cid,Guid id,Actor actor,int offset,int limit,CancellationToken ct)
    {Paging(offset,limit);var c=await Card(cid,id,actor,Permissions.Read,ct);await Scope(cid,actor,Permissions.Audit,c.BranchId,ct);return await store.ReadPage<AuditEvent>(cid,x=>x.ResourceId==id && x.Action.StartsWith("numbering."),q=>q.OrderByDescending(x=>x.OccurredAt).ThenBy(x=>x.Id),offset,limit,ct);}
    public async Task<Page<DocumentNumberUsageRow>> Usage(Guid cid,Guid id,Actor actor,int offset,int limit,CancellationToken ct)
    {
        Paging(offset,limit);var c=await Card(cid,id,actor,Permissions.Read,ct);
        var page=await store.ReadPage<DocumentNumberAllocation>(cid,x=>x.SeriesId==id,q=>q.OrderByDescending(x=>x.Sequence).ThenBy(x=>x.Id),offset,limit,ct);
        var ids=page.Items.Where(x=>x.DocumentId!=null).Select(x=>x.DocumentId!.Value).ToArray();var rows=new List<DocumentNumberUsageRow>();
        var member=await Authorize(cid,actor,Permissions.Read,null,ct);var allowed=await AllowedBranches(cid,member,null,ct);
        var journals=c.DocumentKind==NumberingDocumentKind.Journal?await store.Find<JournalEntry>(cid,x=>ids.Contains(x.Id) && allowed.Contains(x.BranchId),ct):Array.Empty<JournalEntry>();
        var openings=c.DocumentKind==NumberingDocumentKind.InventoryOpening?await store.Find<InventoryOpening>(cid,x=>ids.Contains(x.Id) && allowed.Contains(x.BranchId),ct):Array.Empty<InventoryOpening>();
        foreach(var x in page.Items)
        {var j=journals.SingleOrDefault(a=>a.Id==x.DocumentId);var o=openings.SingleOrDefault(a=>a.Id==x.DocumentId);rows.Add(new(x.Id,x.Sequence,x.Number,x.BranchId,x.ReservedBy,x.ReservedAt,x.DocumentId,j?.State.ToString()??o?.State.ToString()));}
        return new(rows,page.TotalCount,offset,limit);
    }
}
