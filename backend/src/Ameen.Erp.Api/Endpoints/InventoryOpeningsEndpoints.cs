using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;
namespace Ameen.Erp.Api;
internal static class InventoryOpeningsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company + "/inventory-openings";
        const string tag = "INV06 Inventory openings preparation";
        app.MapPost(p, async (Guid companyId, CreateInventoryOpeningRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Create(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        app.MapPut(p + "/{id:guid}", async (Guid companyId, Guid id, EditInventoryOpeningRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Edit(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        app.MapGet(p, async (Guid companyId, Guid? branchId, InventoryOpeningState? state, string? search, int? offset, int? limit, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Search(companyId, Actor(h), branchId, state, search, offset ?? 0, limit ?? 50, ct))).Produces<Page<InventoryOpening>>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/{id:guid}", async (Guid companyId, Guid id, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Get(companyId, id, Actor(h), ct))).Produces<InventoryOpening>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/{id:guid}/valuation-preview", async (Guid companyId, Guid id, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Preview(companyId, id, Actor(h), ct))).Produces<InventoryOpeningPreview>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.AuditEvents(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags(tag);
        foreach (var action in new[] { "submit", "approve", "return", "cancel" })
            app.MapPost(p + "/{id:guid}/" + action, async (Guid companyId, Guid id, TransitionRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
                Results.Ok(await s.Transition(companyId, id, Actor(h), Key(h), r, action, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
    }
}
