using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.AspNetCore.Mvc;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class ReportsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/reports/trial-balance", async (Guid companyId, [FromQuery] string from, [FromQuery] string to,
            Guid? branchId, Guid? costCenterId, int? accountLevel, bool? showZero, ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.TrialBalance(companyId, Actor(h), Date(from), Date(to), branchId, ct, costCenterId, accountLevel, showZero ?? true)))
            .Produces<TrialBalanceResult>().RequireAuthorization().WithTags("RPT01 Trial balance");
        app.MapGet(p + "/reports/accounts/{accountId:guid}/ledger", async (Guid companyId, Guid accountId,
            [FromQuery] string from, [FromQuery] string to, Guid? branchId, int? offset, int? limit, Guid? costCenterId, string? externalReference,
            ErpService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.AccountLedger(companyId, accountId, Actor(h), Date(from), Date(to), branchId, offset ?? 0, limit ?? 50, ct, costCenterId, externalReference)))
            .Produces<AccountLedgerResult>().RequireAuthorization().WithTags("RPT02 Account ledger");
    }
}
