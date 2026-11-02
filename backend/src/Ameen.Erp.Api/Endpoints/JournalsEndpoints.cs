using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class JournalsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/journals/processing-queue", async (Guid companyId, Guid periodId, Guid? branchId, JournalState? state,
            JournalSource? source, long? sequenceFrom, long? sequenceTo, string? numberRange, string? action, int? offset, int? limit, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ProcessingQueue(companyId, Actor(h), periodId, branchId, state, source, sequenceFrom, sequenceTo, action ?? "approve", offset ?? 0, limit ?? 50, ct, numberRange)))
            .Produces<Page<JournalProcessingPreview>>().RequireAuthorization().WithTags("GL05 GL06 Journal processing");
        app.MapGet(p + "/journals/{id:guid}/processing-preview", async (Guid companyId, Guid id, string action, string? reason, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ProcessingPreview(companyId, id, Actor(h), action, reason, ct))).Produces<JournalProcessingPreview>().RequireAuthorization().WithTags("GL05 GL06 Journal processing");
        app.MapPost(p + "/journals/process-batch", async (Guid companyId, ProcessJournalsRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ProcessJournals(companyId, Actor(h), r, ct))).Produces<JournalProcessingResult>().RequireAuthorization().WithTags("GL05 GL06 Journal processing");
        app.MapGet(p + "/journals/processing-batches/by-key/{batchKey}", async (Guid companyId, string batchKey,
            JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.DurableBatch(companyId, Actor(h), batchKey, ct)))
            .Produces<JournalProcessingDurableResult>().RequireAuthorization().WithTags("GL06 Durable processing");
        app.MapGet(p + "/journals/{id:guid}/processing-history", async (Guid companyId, Guid id, int? offset, int? limit, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ProcessingHistory(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("GL05 GL06 Journal processing");
        app.MapPost(p + "/journals/reversals/run-due", async (Guid companyId, RunDueReversalsRequest r, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.RunScheduledReversals(companyId, Actor(h), r.AsOf, r.Limit ?? 50, ct)))
            .Produces<ReversalDueRunResult>().RequireAuthorization().WithTags("GL03 GL07 Scheduled reversals");
        app.MapGet(p + "/journals", async (Guid companyId, Guid? branchId, JournalState? state, JournalSource? source, string? search, int? offset, int? limit, JournalService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListJournals(companyId, Actor(h), branchId, state, offset ?? 0, limit ?? 50, ct, source, search))).Produces<Page<JournalSummary>>().RequireAuthorization().WithTags("GL03 Journals");
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
