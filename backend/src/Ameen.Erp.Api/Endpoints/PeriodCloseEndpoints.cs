using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class PeriodCloseEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/periods/{id:guid}/close-preview", async (Guid companyId, Guid id, int? offset, int? limit, PeriodCloseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.PreviewPeriodClose(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<PeriodClosePreview>().RequireAuthorization().WithTags("GL11 Period close");
        app.MapGet(p + "/periods/{id:guid}/execution-history", async (Guid companyId, Guid id, int? offset, int? limit, PeriodCloseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.PeriodExecutionHistory(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct)))
            .Produces<PeriodExecutionSummary>().RequireAuthorization().WithTags("GL11 Period close");
        app.MapPost(p + "/periods/{id:guid}/close", async (Guid companyId, Guid id, ChangePeriodRequest r, PeriodCloseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ChangePeriod(companyId, id, Actor(h), Key(h), r, true, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL11 Period close");
        app.MapPost(p + "/periods/{id:guid}/reopen", async (Guid companyId, Guid id, ChangePeriodRequest r, PeriodCloseService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ChangePeriod(companyId, id, Actor(h), Key(h), r, false, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL12 Period reopen");
    }
}
