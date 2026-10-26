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
        app.MapGet(p + "/{id:guid}/settlement-preview", async (Guid companyId, Guid id, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.OpeningSettlement(companyId, id, Actor(h), ct))).Produces<GeneralOpeningSettlementPreview>().RequireAuthorization().WithTags("GL02 General openings");
    }
}
