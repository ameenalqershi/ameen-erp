using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class AccountsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/accounts", async (Guid companyId, string? search, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAccounts(companyId, Actor(h), search, offset ?? 0, limit ?? 50, ct))).Produces<Page<Account>>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapPost(p + "/accounts", async (Guid companyId, CreateAccountRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateAccount(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/accounts/{id:guid}", async (Guid companyId, Guid id, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetAccount(companyId, id, Actor(h), ct))).Produces<Account>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapPut(p + "/accounts/{id:guid}", async (Guid companyId, Guid id, EditAccountRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditAccount(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/accounts/{id:guid}/usage", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetAccountUsage(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<AccountUsage>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapGet(p + "/accounts/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListAccountAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("GL01 Accounts");
        app.MapPost(p + "/accounts/{id:guid}/status", async (Guid companyId, Guid id, SetAccountStatusRequest r, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetAccountStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("GL01 Accounts");
    }
}
