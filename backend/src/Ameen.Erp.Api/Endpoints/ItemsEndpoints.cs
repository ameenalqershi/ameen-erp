using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class ItemsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/items", async (Guid companyId, string? search, Guid? groupId, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchItems(companyId, Actor(h), search, groupId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Item>>().RequireAuthorization().WithTags("INV04 Items");
        app.MapPost(p + "/items", async (Guid companyId, CreateItemRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateItem(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV04 Items");
        app.MapGet(p + "/items/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetItem(companyId, id, Actor(h), ct))).Produces<Item>().RequireAuthorization().WithTags("INV04 Items");
        app.MapPut(p + "/items/{id:guid}", async (Guid companyId, Guid id, EditItemRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditItem(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV04 Items");
        app.MapPost(p + "/items/{id:guid}/status", async (Guid companyId, Guid id, SetItemStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetItemStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV04 Items");
        app.MapGet(p + "/items/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListItemAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("INV04 Items");
        app.MapGet(p + "/items/{id:guid}/convert-quantity", async (Guid companyId, Guid id, Guid unitId, string quantity, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ConvertItemQuantity(companyId, id, Actor(h), unitId, ExactQuantity(quantity), ct))).Produces<ItemQuantityConversion>().RequireAuthorization().WithTags("INV04 Items");
        app.MapGet(p + "/units/{id:guid}/items", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListUnitItems(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<Item>>().RequireAuthorization().WithTags("INV04 Items");
    }
}
