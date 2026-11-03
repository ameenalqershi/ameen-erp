using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class CashBoxesEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company + "/cash-boxes";
        app.MapGet(p + "/search", async (Guid companyId, string? search, Guid? branchId,
            bool? isActive, int? offset, int? limit, CashBoxService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchCashBoxes(companyId, Actor(h), search, branchId,
                isActive, offset ?? 0, limit ?? 50, ct)))
            .Produces<Page<CashBox>>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
        app.MapPost(p, async (Guid companyId, CreateCashBoxRequest request,
            CashBoxService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCashBox(companyId, Actor(h), Key(h), request, ct)))
            .Produces<CommandReceipt>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
        app.MapGet(p + "/{id:guid}", async (Guid companyId, Guid id, CashBoxService s,
            HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCashBox(companyId, id, Actor(h), ct)))
            .Produces<CashBox>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
        app.MapPut(p + "/{id:guid}", async (Guid companyId, Guid id, EditCashBoxRequest request,
            CashBoxService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCashBox(companyId, id, Actor(h), Key(h), request, ct)))
            .Produces<CommandReceipt>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
        app.MapPost(p + "/{id:guid}/status", async (Guid companyId, Guid id,
            SetCashBoxStatusRequest request, CashBoxService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCashBoxStatus(companyId, id, Actor(h), Key(h), request, ct)))
            .Produces<CommandReceipt>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
        app.MapGet(p + "/{id:guid}/usage", async (Guid companyId, Guid id,
            CashBoxService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCashBoxUsage(companyId, id, Actor(h), ct)))
            .Produces<CashBoxUsage>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
        app.MapGet(p + "/{id:guid}/audit-events", async (Guid companyId, Guid id,
            int? offset, int? limit, CashBoxService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCashBoxAudit(companyId, id, Actor(h),
                offset ?? 0, limit ?? 50, ct)))
            .Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("CASH01 Cash Boxes");
    }
}
