using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class RecurringJournalsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/recurring-journals", async (Guid companyId, Guid? branchId, bool? isActive, int? offset, int? limit, string? search, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListRecurringJournals(companyId, Actor(h), branchId, isActive, offset ?? 0, limit ?? 50, ct, search))).Produces<Page<RecurringJournalSummary>>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetRecurringJournal(companyId, id, Actor(h), ct))).Produces<RecurringJournalTemplate>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPost(p + "/recurring-journals", async (Guid companyId, CreateRecurringJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateRecurringJournal(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPut(p + "/recurring-journals/{id:guid}", async (Guid companyId, Guid id, EditRecurringJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditRecurringJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPost(p + "/recurring-journals/{id:guid}/status", async (Guid companyId, Guid id, SetRecurringStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetRecurringStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapPost(p + "/recurring-journals/{id:guid}/generate", async (Guid companyId, Guid id, GenerateRecurringJournalRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GenerateRecurringJournal(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}/generations", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListRecurringGenerations(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<RecurringJournalGeneration>>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}/schedule", async (Guid companyId, Guid id, Guid fiscalYearId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.RecurringSchedule(companyId, id, fiscalYearId, Actor(h), ct))).Produces<IReadOnlyList<RecurringScheduleRow>>().RequireAuthorization().WithTags("GL04 Recurring journals");
        app.MapGet(p + "/recurring-journals/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListRecurringChanges(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("GL04 Recurring journals");
    }
}
