using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;
namespace Ameen.Erp.Api;
internal static class GeneralOpeningsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company + "/general-openings";
        app.MapPost(p, async (Guid companyId, CreateGeneralOpeningRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateGeneralOpening(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL02 General openings");
        app.MapPut(p + "/{id:guid}", async (Guid companyId, Guid id, EditGeneralOpeningRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditGeneralOpening(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL02 General openings");
        app.MapGet(p, async (Guid companyId, Guid? branchId, JournalState? state, string? search, int? offset, int? limit, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListJournals(companyId, Actor(h), branchId, state, offset ?? 0, limit ?? 50, ct, JournalSource.GeneralOpening, search))).Produces<Page<JournalSummary>>().RequireAuthorization().WithTags("GL02 General openings");
        app.MapGet(p + "/{id:guid}", async (Guid companyId, Guid id, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetGeneralOpening(companyId, id, Actor(h), ct))).Produces<JournalEntry>().RequireAuthorization().WithTags("GL02 General openings");
        app.MapGet(p + "/{id:guid}/impact-preview", async (Guid companyId, Guid id, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.OpeningImpactPreview(companyId, id, Actor(h), ct)))
            .Produces<JournalImpactPreview>().RequireAuthorization().WithTags("GL02 Opening impact preview");
        app.MapGet(p + "/{id:guid}/settlement-preview", async (Guid companyId, Guid id, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.OpeningSettlement(companyId, id, Actor(h), ct))).Produces<GeneralOpeningSettlementPreview>().RequireAuthorization().WithTags("GL02 General openings");
        app.MapGet(p + "/{id:guid}/print", async (Guid companyId, Guid id, JournalService s, HttpContext h, CancellationToken ct) =>
        {
            var print = await s.GeneralOpeningPrint(companyId, id, Actor(h), ct);
            h.Response.Headers["Cache-Control"] = "no-store, private";
            h.Response.Headers["X-Content-Type-Options"] = "nosniff";
            h.Response.Headers["X-Frame-Options"] = "DENY";
            h.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";
            return Results.Content(GeneralOpeningPrintHtml.Render(print), "text/html; charset=utf-8");
        }).Produces(StatusCodes.Status200OK, contentType: "text/html")
          .RequireAuthorization().WithTags("GL02 General openings");
    }
}
