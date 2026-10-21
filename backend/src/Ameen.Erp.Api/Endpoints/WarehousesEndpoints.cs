using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class WarehousesEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/warehouses", async (Guid companyId, string? search, Guid? branchId, bool? isActive, int? offset, int? limit, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchWarehouses(companyId, Actor(h), search, branchId, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Warehouse>>().RequireAuthorization().WithTags("INV01 Warehouses");
        app.MapPost(p + "/warehouses", async (Guid companyId, CreateWarehouseRequest r, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateWarehouse(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV01 Warehouses");
        app.MapGet(p + "/warehouses/{id:guid}", async (Guid companyId, Guid id, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetWarehouse(companyId, id, Actor(h), ct))).Produces<Warehouse>().RequireAuthorization().WithTags("INV01 Warehouses");
        app.MapPut(p + "/warehouses/{id:guid}", async (Guid companyId, Guid id, EditWarehouseRequest r, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditWarehouse(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV01 Warehouses");
        app.MapPost(p + "/warehouses/{id:guid}/status", async (Guid companyId, Guid id, SetWarehouseStatusRequest r, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetWarehouseStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV01 Warehouses");
        app.MapGet(p + "/warehouses/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListWarehouseAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("INV01 Warehouses");
        app.MapGet(p + "/accounts/{id:guid}/warehouses", async (Guid companyId, Guid id, int? offset, int? limit, WarehouseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAccountWarehouses(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<Warehouse>>().RequireAuthorization().WithTags("INV01 Warehouses");
    }
}
