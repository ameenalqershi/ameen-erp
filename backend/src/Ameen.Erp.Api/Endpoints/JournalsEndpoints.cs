using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class JournalsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/journals", async (Guid companyId, Guid? branchId, JournalState? state, JournalSource? source, int? offset, int? limit, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListJournals(companyId, Actor(h), branchId, state, offset ?? 0, limit ?? 50, ct, source))).Produces<Page<JournalSummary>>().RequireAuthorization().WithTags("GL03 Journals");
        app.MapGet(p + "/journals/{id:guid}", async (Guid companyId, Guid id, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetJournal(companyId, id, Actor(h), ct))).Produces<JournalEntry>().RequireAuthorization().WithTags("GL03 Journals");
        app.MapPost(p + "/journals", async (Guid companyId, CreateJournalRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateJournal(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL03 Journals");
        app.MapPut(p + "/journals/{id:guid}", async (Guid companyId, Guid id, EditJournalRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL03 Journals");
        foreach (var transition in new[] { "submit", "approve", "return", "post", "cancel" })
        {
            var action = transition;
            app.MapPost(p + "/journals/{id:guid}/" + action,
                async (Guid companyId, Guid id, TransitionRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
                    Results.Ok(await s.TransitionJournal(companyId, id, Actor(h), Key(h), r, action, ct)))
                .Produces<CommandReceipt>().RequireAuthorization().WithTags("GL05 GL06 Journal workflow");
        }
        app.MapPost(p + "/journals/{id:guid}/reverse", async (Guid companyId, Guid id, ReverseJournalRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ReverseJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL07 Reversals");
    }
}
