using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class FiscalCalendarEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/fiscal-years", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<FiscalYear>(companyId, Actor(h), ct))).Produces<IReadOnlyList<FiscalYear>>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapPost(p + "/fiscal-years", async (Guid companyId, CreateFiscalYearRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateFiscalYear(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapGet(p + "/fiscal-years/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetFiscalCalendar(companyId, id, Actor(h), ct))).Produces<FiscalCalendar>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapGet(p + "/fiscal-years/search", async (Guid companyId, string? search, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchFiscalYears(companyId, Actor(h), search, offset ?? 0, limit ?? 50, ct))).Produces<Page<FiscalYear>>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapGet(p + "/fiscal-years/{id:guid}/usage", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetFiscalYearUsage(companyId, id, Actor(h), ct))).Produces<FiscalYearUsage>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapGet(p + "/fiscal-years/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListFiscalYearAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
        app.MapGet(p + "/periods", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<FiscalPeriod>(companyId, Actor(h), ct))).Produces<IReadOnlyList<FiscalPeriod>>().RequireAuthorization().WithTags("SYS03 Fiscal calendar");
    }
}
