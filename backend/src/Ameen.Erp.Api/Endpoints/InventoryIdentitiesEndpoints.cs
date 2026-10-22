using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class InventoryIdentitiesEndpoints
{
    public static void Map(WebApplication app)
    {
        MapTracking(app, "lots", ItemTracking.Lot, "INV05 Lots");
        MapTracking(app, "serials", ItemTracking.Serial, "INV05 Serials");
    }

    private static void MapTracking(WebApplication app, string resource, ItemTracking tracking, string tag)
    {
        const string p = EndpointRoutes.Company;
        var isLot = tracking == ItemTracking.Lot;
        app.MapGet(p + "/items/{itemId:guid}/" + resource,
            async (Guid companyId, Guid itemId, string? search, InventoryQualityStatus? qualityStatus, bool? isActive,
                int? offset, int? limit, InventoryIdentityService s, HttpContext h, CancellationToken ct) =>
                Results.Ok(isLot
                    ? await s.SearchLots(companyId, itemId, Actor(h), search, qualityStatus, isActive, offset ?? 0, limit ?? 50, ct)
                    : await s.SearchSerials(companyId, itemId, Actor(h), search, qualityStatus, isActive, offset ?? 0, limit ?? 50, ct)))
            .Produces<Page<InventoryIdentity>>().RequireAuthorization().WithTags(tag);

        app.MapPost(p + "/items/{itemId:guid}/" + resource,
            async (Guid companyId, Guid itemId, CreateInventoryIdentityRequest r, InventoryIdentityService s,
                HttpContext h, CancellationToken ct) =>
                Results.Ok(isLot
                    ? await s.CreateLot(companyId, itemId, Actor(h), Key(h), r, ct)
                    : await s.CreateSerial(companyId, itemId, Actor(h), Key(h), r, ct)))
            .Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);

        app.MapGet(p + "/items/{itemId:guid}/" + resource + "/{id:guid}",
            async (Guid companyId, Guid itemId, Guid id, InventoryIdentityService s, HttpContext h, CancellationToken ct) =>
                Results.Ok(isLot
                    ? await s.GetLot(companyId, itemId, id, Actor(h), ct)
                    : await s.GetSerial(companyId, itemId, id, Actor(h), ct)))
            .Produces<InventoryIdentity>().RequireAuthorization().WithTags(tag);

        app.MapPut(p + "/items/{itemId:guid}/" + resource + "/{id:guid}",
            async (Guid companyId, Guid itemId, Guid id, EditInventoryIdentityRequest r, InventoryIdentityService s,
                HttpContext h, CancellationToken ct) =>
                Results.Ok(isLot
                    ? await s.EditLot(companyId, itemId, id, Actor(h), Key(h), r, ct)
                    : await s.EditSerial(companyId, itemId, id, Actor(h), Key(h), r, ct)))
            .Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);

        app.MapPost(p + "/items/{itemId:guid}/" + resource + "/{id:guid}/status",
            async (Guid companyId, Guid itemId, Guid id, SetInventoryIdentityStatusRequest r,
                InventoryIdentityService s, HttpContext h, CancellationToken ct) =>
                Results.Ok(isLot
                    ? await s.SetLotStatus(companyId, itemId, id, Actor(h), Key(h), r, ct)
                    : await s.SetSerialStatus(companyId, itemId, id, Actor(h), Key(h), r, ct)))
            .Produces<CommandReceipt>().RequireAuthorization().WithTags(tag);

        app.MapGet(p + "/items/{itemId:guid}/" + resource + "/{id:guid}/audit-events",
            async (Guid companyId, Guid itemId, Guid id, int? offset, int? limit, InventoryIdentityService s,
                HttpContext h, CancellationToken ct) =>
                Results.Ok(isLot
                    ? await s.ListLotAudit(companyId, itemId, id, Actor(h), offset ?? 0, limit ?? 50, ct)
                    : await s.ListSerialAudit(companyId, itemId, id, Actor(h), offset ?? 0, limit ?? 50, ct)))
            .Produces<Page<AuditEvent>>().RequireAuthorization().WithTags(tag);
    }
}
