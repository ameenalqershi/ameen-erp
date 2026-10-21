using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class AuditEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/audit-events", async (Guid companyId, int? offset, int? limit, string? from, string? to, string? actorId, string? action, Guid? resourceId, AuditService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAudit(companyId, Actor(h), offset ?? 0, limit ?? 50, ct, from is null ? null : Date(from), to is null ? null : Date(to), actorId, action, resourceId))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS14 Audit");
        app.MapGet(p + "/audit-events/{id:guid}", async (Guid companyId, Guid id, AuditService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetAuditEvent(companyId, id, Actor(h), ct))).Produces<AuditEvent>().RequireAuthorization().WithTags("SYS14 Audit");
    }
}
