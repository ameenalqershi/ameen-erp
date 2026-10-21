using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class CurrenciesEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/currencies", async (Guid companyId, SetupQueryService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<Currency>(companyId, Actor(h), ct))).Produces<IReadOnlyList<Currency>>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapPost(p + "/currencies", async (Guid companyId, CreateCurrencyRequest r, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCurrency(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/search", async (Guid companyId, string? search, bool? isActive, int? offset, int? limit, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchCurrencies(companyId, Actor(h), search, isActive, offset ?? 0, limit ?? 50, ct))).Produces<Page<Currency>>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/{id:guid}", async (Guid companyId, Guid id, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCurrency(companyId, id, Actor(h), ct))).Produces<Currency>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapPut(p + "/currencies/{id:guid}", async (Guid companyId, Guid id, EditCurrencyRequest r, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCurrency(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapPost(p + "/currencies/{id:guid}/status", async (Guid companyId, Guid id, SetCurrencyStatusRequest r, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCurrencyStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/{id:guid}/usage", async (Guid companyId, Guid id, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCurrencyUsage(companyId, id, Actor(h), ct))).Produces<CurrencyUsage>().RequireAuthorization().WithTags("SYS04 Currencies");
        app.MapGet(p + "/currencies/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, CurrencyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCurrencyAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS04 Currencies");
    }
}
