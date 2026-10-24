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
        app.MapGet(p + "/reports/inventory-balances", async (Guid companyId, [FromQuery] string from, [FromQuery] string to,
            Guid? branchId, Guid? warehouseId, Guid? groupId, Guid? itemId, bool? showZero, int? offset, int? limit,
            ReportService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.InventoryBalances(companyId, Actor(h), Date(from), Date(to), branchId, warehouseId, groupId, itemId, showZero ?? true, offset ?? 0, limit ?? 50, ct)))
            .Produces<InventoryBalanceResult>().RequireAuthorization().WithTags("RPT08 Inventory balances");
        app.MapGet(p + "/reports/items/{itemId:guid}/ledger", async (Guid companyId, Guid itemId, [FromQuery] string from, [FromQuery] string to,
            Guid? branchId, Guid? warehouseId, string? identityNumber, int? offset, int? limit,
            ReportService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.InventoryLedger(companyId, itemId, Actor(h), Date(from), Date(to), branchId, warehouseId, identityNumber, offset ?? 0, limit ?? 50, ct)))
            .Produces<InventoryLedgerResult>().RequireAuthorization().WithTags("RPT09 Item movement ledger");
        app.MapGet(p + "/reports/trial-balance", async (Guid companyId, [FromQuery] string from, [FromQuery] string to,
            Guid? branchId, Guid? costCenterId, int? accountLevel, bool? showZero, ReportService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.TrialBalance(companyId, Actor(h), Date(from), Date(to), branchId, ct, costCenterId, accountLevel, showZero ?? true)))
            .Produces<TrialBalanceResult>().RequireAuthorization().WithTags("RPT01 Trial balance");
        app.MapGet(p + "/reports/accounts/{accountId:guid}/ledger", async (Guid companyId, Guid accountId,
            [FromQuery] string from, [FromQuery] string to, Guid? branchId, int? offset, int? limit, Guid? costCenterId, string? externalReference,
            ReportService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.AccountLedger(companyId, accountId, Actor(h), Date(from), Date(to), branchId, offset ?? 0, limit ?? 50, ct, costCenterId, externalReference)))
            .Produces<AccountLedgerResult>().RequireAuthorization().WithTags("RPT02 Account ledger");
    }
}
