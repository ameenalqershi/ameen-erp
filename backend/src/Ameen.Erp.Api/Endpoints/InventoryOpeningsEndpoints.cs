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
        app.MapGet(p + "/{id:guid}/print", async (Guid companyId, Guid id,
            InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Print(companyId, id, Actor(h), ct)))
            .Produces<InventoryOpeningPrintDocument>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/{id:guid}/valuation-preview", async (Guid companyId, Guid id, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Preview(companyId, id, Actor(h), ct))).Produces<InventoryOpeningPreview>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.AuditEvents(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags(tag);
        app.MapPost(p + "/{id:guid}/post", async (Guid companyId, Guid id, PostInventoryOpeningRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Post(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/posting-settings", async (Guid companyId, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetPostingSettings(companyId, Actor(h), ct))).Produces<InventoryOpeningPostingSettings>().RequireAuthorization().WithTags(tag);
        app.MapPut(p + "/posting-settings", async (Guid companyId, ConfigureInventoryOpeningPostingRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ConfigurePosting(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        app.MapGet(EndpointRoutes.Company + "/stock-movements", async (Guid companyId, Guid? warehouseId, Guid? itemId, int? offset, int? limit, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Movements(companyId, Actor(h), warehouseId, itemId, offset ?? 0, limit ?? 50, ct))).Produces<Page<StockMovement>>().RequireAuthorization().WithTags(tag);
        app.MapGet(p + "/{id:guid}/reversals", async (Guid companyId, Guid id, int? offset, int? limit, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.Reversals(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<InventoryOpening>>().RequireAuthorization().WithTags(tag);
        app.MapPost(p + "/{id:guid}/reversals", async (Guid companyId, Guid id, ReverseInventoryOpeningRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateReversal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        app.MapPost(p + "/{id:guid}/correct-reversal", async (Guid companyId, Guid id, TransitionRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CorrectReversal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        app.MapPost(p + "/{id:guid}/post-reversal", async (Guid companyId, Guid id, TransitionRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.PostReversal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
        foreach (var action in new[] { "submit", "approve", "return", "cancel" })
            app.MapPost(p + "/{id:guid}/" + action, async (Guid companyId, Guid id, TransitionRequest r, InventoryOpeningService s, HttpContext h, CancellationToken ct) =>
                Results.Ok(await s.Transition(companyId, id, Actor(h), Key(h), r, action, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);
    }
}
