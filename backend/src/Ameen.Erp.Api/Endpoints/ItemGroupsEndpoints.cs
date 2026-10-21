using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class ItemGroupsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/item-groups", async (Guid companyId, string? search, Guid? parentId, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchItemGroups(companyId, Actor(h), search, parentId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<ItemGroup>>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapGet(p + "/item-groups/{id:guid}/usage", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetItemGroupUsage(companyId, id, Actor(h), ct))).Produces<ItemGroupUsage>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapGet(p + "/item-groups/{id:guid}/items", async (Guid companyId, Guid id, bool? includeDescendants, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListItemGroupItems(companyId, id, Actor(h), includeDescendants ?? true, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Item>>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapPost(p + "/item-groups", async (Guid companyId, CreateItemGroupRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateItemGroup(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapGet(p + "/item-groups/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetItemGroup(companyId, id, Actor(h), ct))).Produces<ItemGroup>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapPut(p + "/item-groups/{id:guid}", async (Guid companyId, Guid id, EditItemGroupRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditItemGroup(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapPost(p + "/item-groups/{id:guid}/status", async (Guid companyId, Guid id, SetItemGroupStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetItemGroupStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapGet(p + "/item-groups/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListItemGroupAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("INV02 Item groups");
        app.MapGet(p + "/accounts/{id:guid}/item-groups", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAccountItemGroups(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<ItemGroup>>().RequireAuthorization().WithTags("INV02 Item groups");
    }
}
