using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class UnitsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/units", async (Guid companyId, string? search, bool? isActive, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchUnits(companyId, Actor(h), search, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<UnitOfMeasure>>().RequireAuthorization().WithTags("INV03 Units");
        app.MapPost(p + "/units", async (Guid companyId, CreateUnitRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateUnit(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV03 Units");
        app.MapGet(p + "/units/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetUnit(companyId, id, Actor(h), ct))).Produces<UnitOfMeasure>().RequireAuthorization().WithTags("INV03 Units");
        app.MapPut(p + "/units/{id:guid}", async (Guid companyId, Guid id, EditUnitRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditUnit(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV03 Units");
        app.MapPost(p + "/units/{id:guid}/status", async (Guid companyId, Guid id, SetUnitStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetUnitStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("INV03 Units");
        app.MapGet(p + "/units/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListUnitAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("INV03 Units");
    }
}
