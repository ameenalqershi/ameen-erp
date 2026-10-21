using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class ExchangeRatesEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/exchange-rates", async (Guid companyId, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListSetup<ExchangeRate>(companyId, Actor(h), ct))).Produces<IReadOnlyList<ExchangeRate>>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapPost(p + "/exchange-rates", async (Guid companyId, CreateExchangeRateRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateExchangeRate(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapGet(p + "/exchange-rates/search", async (Guid companyId, string? currencyCode, DateOnly? from, DateOnly? to, string? source, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SearchExchangeRates(companyId, Actor(h), currencyCode, from, to, source, offset ?? 0, limit ?? 50, ct))).Produces<Page<ExchangeRate>>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapGet(p + "/exchange-rates/quote", async (Guid companyId, string currencyCode, DateOnly date, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.QuoteExchangeRate(companyId, Actor(h), currencyCode, date, ct))).Produces<ExchangeRateQuote>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapGet(p + "/exchange-rates/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetExchangeRate(companyId, id, Actor(h), ct))).Produces<ExchangeRateDetail>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapGet(p + "/exchange-rates/{id:guid}/usage", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetExchangeRateUsage(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AccountUsageReference>>().RequireAuthorization().WithTags("SYS05 Exchange rates");
        app.MapGet(p + "/exchange-rates/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListExchangeRateAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS05 Exchange rates");
    }
}
