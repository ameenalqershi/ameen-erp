using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using static Ameen.Erp.Api.ApiSupport;

namespace Ameen.Erp.Api;

internal static class MembershipsEndpoints
{
    public static void Map(WebApplication app)
    {
        const string p = EndpointRoutes.Company;
        app.MapGet(p + "/memberships", async (Guid companyId, int? offset, int? limit, MembershipService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListMembers(companyId, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<Membership>>().RequireAuthorization().WithTags("SYS07 SYS08 Memberships");
        app.MapPost(p + "/memberships", async (Guid companyId, GrantMembershipRequest r, MembershipService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GrantMembership(companyId, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS07 SYS08 Memberships");
        app.MapGet(p + "/memberships/{id:guid}/usage", async (Guid companyId, Guid id, MembershipService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.GetMembershipUsage(companyId, id, Actor(h), ct))).Produces<MembershipUsage>().RequireAuthorization().WithTags("SYS06 Membership usage");
        app.MapGet(p + "/memberships/{id:guid}/audit-events", async (Guid companyId, Guid id, int? offset, int? limit, MembershipService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.ListMembershipAudit(companyId, id, Actor(h), offset ?? 0, limit ?? 50, ct))).Produces<Page<AuditEvent>>().RequireAuthorization().WithTags("SYS06 Membership audit");
        app.MapPost(p + "/memberships/{id:guid}/status", async (Guid companyId, Guid id, SetMembershipStatusRequest r, MembershipService s, HttpContext h, CancellationToken ct) =>
            Results.Ok(await s.SetMembershipStatus(companyId, id, Actor(h), Key(h), r, ct))).Produces<CommandReceipt>().RequireAuthorization().WithTags("SYS06 Membership status");
    }
}
