using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class BranchesEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/branches", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListBranches(companyId, Actor(h), ct))).Produces<IReadOnlyList<Branch>>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapPost(p + "/branches", async (Guid companyId, CreateBranchRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateBranch(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetBranch(companyId, id, Actor(h), ct))).Produces<Branch>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapPut(p + "/branches/{id:guid}", async (Guid companyId, Guid id, EditBranchRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditBranch(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapPost(p + "/branches/{id:guid}/status", async (Guid companyId, Guid id, SetBranchStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetBranchStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/{id:guid}/usage", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetBranchUsage(companyId, id, Actor(h), ct))).Produces<BranchUsage>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListBranchAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS02 Branches");
        app.MapGet(p + "/branches/search", async (Guid companyId, string? search, Guid? parentId, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchBranches(companyId, Actor(h), search, parentId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Branch>>().RequireAuthorization().WithTags("SYS02 Branches");
    }
}
