using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed partial class NumberingService : ApplicationService
{
    public NumberingService(IErpStore store,ITransactionRunner tx,IClock clock):base(store,tx,clock) { }
    private async Task Scope(Guid cid,Actor actor,string permission,Guid? branch,CancellationToken ct)
    {
        var member=await Authorize(cid,actor,permission,branch,ct);
        Check.That(branch is not null || member.AllBranches,"all_branches_required","النطاق العام يتطلب تفويض جميع الفروع.",ErrorKind.Forbidden);
        if(branch is Guid id) await Required<Branch>(cid,id,ct);
    }
    private async Task<DocumentNumberSeries> Card(Guid cid,Guid id,Actor actor,string permission,CancellationToken ct)
    {
        await Authorize(cid,actor,permission,null,ct);var card=await Required<DocumentNumberSeries>(cid,id,ct);
        await Scope(cid,actor,permission,card.BranchId,ct);return card;
    }
    private async Task ValidateYear(Guid cid,Guid yearId,Guid? branch,CancellationToken ct)
    {
        await Required<FiscalYear>(cid,yearId,ct);
        Check.That((await store.Find<FiscalPeriod>(cid,x=>x.FiscalYearId==yearId && !x.IsClosed,ct)).Any(),"numbering_year_closed","لا توجد فترة مفتوحة في سنة الترقيم.",ErrorKind.Conflict);
        if(branch is Guid id) Check.That((await Required<Branch>(cid,id,ct)).IsActive,"branch_inactive","الفرع معطل.",ErrorKind.Conflict);
    }
    private static void NonTax(NumberingDocumentKind kind)
    {
        Check.That(Enum.IsDefined(kind),"invalid_numbering_kind","نوع المستند غير صالح.");
        Check.That(kind!=NumberingDocumentKind.TaxInvoice,"tax_numbering_profile_required","الترقيم الضريبي يتطلب ملف الدولة ووحدة TAX05 المعتمدة؛ لا تُختلق سلسلة ضريبية.",ErrorKind.Conflict);
    }
    private async Task Unused(Guid cid,NumberingDocumentKind kind,string number,CancellationToken ct)
    {
        var reserved=(await store.Find<DocumentNumberAllocation>(cid,x=>x.DocumentKind==kind && x.Number==number,ct)).Any();
        var actual=kind==NumberingDocumentKind.Journal
            ? (await store.Find<JournalEntry>(cid,x=>x.Number==number,ct)).Any()
            : (await store.Find<InventoryOpening>(cid,x=>x.Number==number,ct)).Any();
        Check.That(!reserved && !actual,"document_number_used","الرقم محجوز أو مستخدم ولو أُلغي مستنده؛ غيّر البادئة أو ارفع الرقم التالي.",ErrorKind.Conflict);
    }
    public Task<CommandReceipt> Create(Guid cid,Actor actor,string key,CreateNumberSeriesRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"numbering.create",r,t=>Scope(cid,actor,Permissions.Configure,r.BranchId,t),async t=>
        {
            NonTax(r.DocumentKind);await ValidateYear(cid,r.FiscalYearId,r.BranchId,t);
            Check.That(!(await store.Find<DocumentNumberSeries>(cid,x=>x.DocumentKind==r.DocumentKind && x.FiscalYearId==r.FiscalYearId && x.BranchId==r.BranchId,t)).Any(),"duplicate_numbering_scope","توجد سلسلة لهذا النوع والسنة والنطاق.",ErrorKind.Conflict);
            var card=new DocumentNumberSeries(cid,r.BranchId,r.DocumentKind,r.FiscalYearId,r.Prefix,r.NextNumber,actor.Subject,clock.UtcNow);
            await Unused(cid,card.DocumentKind,card.Candidate(),t);store.Add(card);
            Audit(cid,actor,"numbering.created",card,card);return Receipt(card,"Active");
        },ct);
    public Task<CommandReceipt> Edit(Guid cid,Guid id,Actor actor,string key,EditNumberSeriesRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"numbering.edit:"+id,r,t=>Card(cid,id,actor,Permissions.Configure,t),async t=>
        {
            var card=await Required<DocumentNumberSeries>(cid,id,t);Check.Version(card,r.ExpectedVersion);await ValidateYear(cid,card.FiscalYearId,card.BranchId,t);
            var before=new {card.Prefix,card.NextNumber,card.Revision,card.LastModifiedAt};card.Edit(r.Prefix,r.NextNumber,clock.UtcNow);
            await Unused(cid,card.DocumentKind,card.Candidate(),t);Audit(cid,actor,"numbering.edited",card,new {before,after=card});return Receipt(card,card.IsActive?"Active":"Inactive");
        },ct);
    public Task<CommandReceipt> Status(Guid cid,Guid id,Actor actor,string key,SetNumberSeriesStatusRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"numbering.status:"+id,r,t=>Card(cid,id,actor,Permissions.Configure,t),async t=>
        {var card=await Required<DocumentNumberSeries>(cid,id,t);Check.Version(card,r.ExpectedVersion);var reason=Check.Text(r.Reason,"السبب",500);card.SetActive(r.IsActive,clock.UtcNow);Audit(cid,actor,"numbering.status-changed",card,new {card.IsActive,card.Revision,card.LastModifiedAt,reason});return Receipt(card,card.IsActive?"Active":"Inactive");},ct);
    // Participates in the caller's existing company transaction; never creates a nested transaction.
    internal async Task<(long Sequence,string Number,DocumentNumberAllocation Allocation)?> TryAllocate(Guid cid,NumberingDocumentKind kind,Guid yearId,Guid branchId,string actor,CancellationToken ct)
    {
        var candidates=await store.Find<DocumentNumberSeries>(cid,x=>x.DocumentKind==kind && x.FiscalYearId==yearId && (x.BranchId==null || x.BranchId==branchId),ct);
        var card=candidates.OrderByDescending(x=>x.BranchId.HasValue).SingleOrDefault(x=>x.BranchId==branchId)??candidates.SingleOrDefault(x=>x.BranchId==null);
        if(card is null)return null;
        var allocation=await Allocate(card,branchId,actor,ct);return(allocation.Sequence,allocation.Number,allocation);
    }
    private async Task<DocumentNumberAllocation> Allocate(DocumentNumberSeries card,Guid branchId,string actor,CancellationToken ct)
    {
        NonTax(card.DocumentKind);await ValidateYear(card.CompanyId,card.FiscalYearId,branchId,ct);
        Check.That(card.BranchId is null || card.BranchId==branchId,"numbering_branch_mismatch","السلسلة لا تخص فرع المستند.");
        await Unused(card.CompanyId,card.DocumentKind,card.Candidate(),ct);var version=card.Version;
        var n=card.Allocate(clock.UtcNow);var a=new DocumentNumberAllocation(card,branchId,version,n.Sequence,n.Number,actor,clock.UtcNow);store.Add(a);
        Audit(card.CompanyId,new Actor(actor),"numbering.allocated",card,new {a.Id,a.Sequence,a.Number,a.BranchId,card.Revision,card.NextNumber});return a;
    }
    public Task<CommandReceipt> Reserve(Guid cid,Guid id,Actor actor,string key,ReserveDocumentNumberRequest r,CancellationToken ct)=>
        Command(cid,actor,key,"numbering.reserve:"+id,r,async t=>{await Card(cid,id,actor,Permissions.Configure,t);await Authorize(cid,actor,Permissions.Configure,r.BranchId,t);},async t=>
        {var card=await Required<DocumentNumberSeries>(cid,id,t);Check.Version(card,r.ExpectedVersion);var a=await Allocate(card,r.BranchId,actor.Subject,t);return Receipt(a,"Reserved",a.Number);},ct);
}
