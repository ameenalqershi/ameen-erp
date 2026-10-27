using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;
namespace Ameen.Erp.Api;
internal static class NumberingEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p=EndpointRoutes.Company+"/number-series";
        app.MapGet(p,async(Guid companyId,Guid? branchId,bool? isActive,string? search,int? offset,int? limit,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Search(companyId,Actor(h),branchId,isActive,search,offset??0,limit??50,ct))).Produces<Page<DocumentNumberSeries>>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapPost(p,async(Guid companyId,CreateNumberSeriesRequest r,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Create(companyId,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapGet(p+"/{id:guid}",async(Guid companyId,Guid id,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Get(companyId,id,Actor(h),ct))).Produces<DocumentNumberSeries>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapPut(p+"/{id:guid}",async(Guid companyId,Guid id,EditNumberSeriesRequest r,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Edit(companyId,id,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapPost(p+"/{id:guid}/status",async(Guid companyId,Guid id,SetNumberSeriesStatusRequest r,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Status(companyId,id,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapPost(p+"/{id:guid}/reserve",async(Guid companyId,Guid id,ReserveDocumentNumberRequest r,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Reserve(companyId,id,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapGet(p+"/{id:guid}/usage",async(Guid companyId,Guid id,int? offset,int? limit,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Usage(companyId,id,Actor(h),offset??0,limit??50,ct))).Produces<Page<DocumentNumberUsageRow>>().RequireAuthorization().WithTags("SYS10 Numbering");
        app.MapGet(p+"/{id:guid}/audit-events",async(Guid companyId,Guid id,int? offset,int? limit,NumberingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Changes(companyId,id,Actor(h),offset??0,limit??50,ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS10 Numbering");
    }
}
