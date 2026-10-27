using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;
namespace Ameen.Erp.Api;
internal static class FinancialMappingsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p=EndpointRoutes.Company+"/financial-mappings";
        app.MapGet(p,async(Guid companyId,Guid? branchId,FinancialStatementKind? statementKind,bool? isActive,string? search,int? offset,int? limit,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Search(companyId,Actor(h),branchId,statementKind,isActive,search,offset??0,limit??50,ct))).Produces<Page<FinancialMapping>>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapPost(p,async(Guid companyId,CreateFinancialMappingRequest r,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Create(companyId,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapGet(p+"/{id:guid}",async(Guid companyId,Guid id,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Get(companyId,id,Actor(h),ct))).Produces<FinancialMapping>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapPut(p+"/{id:guid}",async(Guid companyId,Guid id,EditFinancialMappingRequest r,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Edit(companyId,id,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapPost(p+"/{id:guid}/status",async(Guid companyId,Guid id,SetFinancialMappingStatusRequest r,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Status(companyId,id,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapPost(p+"/{id:guid}/review",async(Guid companyId,Guid id,ReviewFinancialMappingRequest r,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Review(companyId,id,Actor(h),Key(h),r,ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapGet(p+"/{id:guid}/usage",async(Guid companyId,Guid id,int? offset,int? limit,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Usage(companyId,id,Actor(h),offset??0,limit??50,ct))).Produces<Page<FinancialMappingReviewSummary>>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapGet(p+"/{id:guid}/audit-events",async(Guid companyId,Guid id,int? offset,int? limit,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.Changes(companyId,id,Actor(h),offset??0,limit??50,ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("GL13 Financial mappings");
        app.MapGet(p+"/reviews/{id:guid}",async(Guid companyId,Guid id,FinancialMappingService s,HttpContext h,CancellationToken ct)=>Results.Ok(await s.ReviewDetail(companyId,id,Actor(h),ct))).Produces<FinancialMappingReviewDetail>().RequireAuthorization().WithTags("GL13 Financial mappings");
    }
}
