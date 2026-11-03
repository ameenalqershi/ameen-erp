using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// SYS06 isolated SQL acceptance: deactivation is durable, history stays, and replay is atomic.
public static class MembershipStatusSqlIntegration
{
    public static async Task Run(string connection, Guid companyId, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Context();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var actor = new Actor("sys06-sql-revoked");
        var grant = await Call(s => s.Membership.GrantMembership(companyId, owner, Fixture.Key(),
            new(actor.Subject, new[] { Permissions.Read, Permissions.Members },
                true, Array.Empty<Guid>(), null), default));
        var statusKey = Fixture.Key();
        var request = new SetMembershipStatusRequest(grant.Version, false, "تعطيل مستخدم");
        var inactive = await Call(s => s.Membership.SetMembershipStatus(companyId,
            grant.ResourceId, owner, statusKey, request, default));
        var replay = await Call(s => s.Membership.SetMembershipStatus(companyId,
            grant.ResourceId, owner, statusKey, request, default));
        if (inactive != replay) throw new Exception("SYS06 SQL replay changed disabled receipt");

        Guid disabledVersion;
        await using (var db = Context())
        {
            var member = await db.Set<Membership>().SingleAsync(x =>
                x.CompanyId == companyId && x.Id == grant.ResourceId);
            disabledVersion = member.Version;
            if (member.IsActive || member.Subject != actor.Subject || member.Version != inactive.Version ||
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId &&
                    x.ResourceId == member.Id && x.Action == "membership.status-changed") != 1 ||
                await db.Set<IdempotencyRecord>().CountAsync(x =>
                    x.CompanyId == companyId && x.Key == statusKey) != 1)
                throw new Exception("SYS06 SQL deactivate lost identity, revision, audit or single receipt");
        }
        try
        {
            await Call(s => s.Membership.ListMembers(companyId, actor, 0, 10, default));
            throw new Exception("SYS06 disabled SQL actor retained Members read access");
        }
        catch (BusinessException ex) when (ex.Code == "permission_denied") { }

        var active = await Call(s => s.Membership.SetMembershipStatus(companyId,
            grant.ResourceId, owner, Fixture.Key(),
            new(disabledVersion, true, "إعادة تفعيل"), default));
        await using (var db = Context())
        {
            var member = await db.Set<Membership>().SingleAsync(x =>
                x.CompanyId == companyId && x.Id == grant.ResourceId);
            if (!member.IsActive || member.Version != active.Version ||
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == companyId &&
                    x.ResourceId == member.Id && x.Action == "membership.status-changed") != 2)
                throw new Exception("SYS06 SQL reactivation lost same member or audit history");
        }
        if (!(await Call(s => s.Membership.ListMembers(companyId, actor, 0, 10, default)))
            .Items.Any(x => x.Id == grant.ResourceId && x.IsActive))
            throw new Exception("SYS06 explicit SQL reactivation did not restore authorized access");
        Console.WriteLine("  SQL PASS SYS06 stable membership, revoked JWT access, replay, audit and reactivation");
    }
}
