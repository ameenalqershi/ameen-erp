using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class CompaniesEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet("/api/v1/companies", async (string? search, CompanyService s, HttpContext h, CancellationToken ct) =>
        {
            // Always scope by membership before applying the optional search filter.
            var visible = await s.ListCompanies(Actor(h), ct);
            var term = search?.Trim();
            return Results.Ok(string.IsNullOrEmpty(term) ? visible : visible.Where(c =>
                c.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray());
        }).Produces<IReadOnlyList<Company>>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapPost("/api/v1/companies", async (CreateCompanyRequest r, CompanyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.CreateCompany(Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapGet(p, async (Guid companyId, CompanyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCompany(companyId, Actor(h), ct))).Produces<Company>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapPut(p, async (Guid companyId, EditCompanyRequest r, CompanyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.EditCompany(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapPost(p + "/status", async (Guid companyId, SetCompanyStatusRequest r, CompanyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetCompanyStatus(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapGet(p + "/usage", async (Guid companyId, CompanyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetCompanyUsage(companyId, Actor(h), ct))).Produces<CompanyUsage>().RequireAuthorization().WithTags("SYS01 Companies");
        app.MapGet(p + "/profile-audit-events", async (Guid companyId, int? offset, int? limit, CompanyService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListCompanyAudit(companyId, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS01 Companies");
    }
}
