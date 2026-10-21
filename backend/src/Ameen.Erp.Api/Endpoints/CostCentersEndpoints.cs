using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class CostCentersEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/cost-centers", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<CostCenter>(companyId, Actor(h), ct))).Produces<IReadOnlyList<CostCenter>>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapPost(p + "/cost-centers", async (Guid companyId, CreateCostCenterRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCostCenter(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/search", async (Guid companyId, string? search, Guid? branchId, Guid? parentId, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchCostCenters(companyId, Actor(h), search, branchId, parentId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<CostCenter>>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCostCenter(companyId, id, Actor(h), ct))).Produces<CostCenter>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapPut(p + "/cost-centers/{id:guid}", async (Guid companyId, Guid id, EditCostCenterRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCostCenter(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapPost(p + "/cost-centers/{id:guid}/status", async (Guid companyId, Guid id, SetCostCenterStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCostCenterStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/{id:guid}/usage", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCostCenterUsage(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<CostCenterUsage>().RequireAuthorization().WithTags("COST01 Cost centers");
        app.MapGet(p + "/cost-centers/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCostCenterAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("COST01 Cost centers");
    }
}
