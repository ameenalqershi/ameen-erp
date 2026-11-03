using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class MembershipUsageSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> action)
        {
            await using var db = Context();
            return await action(new TestServices(new SqlErpStore(db),
                new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var readOnly = new Actor("sys06-op03-sql-reader");
        var member = await Call(s => s.Membership.GrantMembership(cid, owner, Fixture.Key(),
            new(readOnly.Subject, new[] { Permissions.Read, Permissions.Members }, true, Array.Empty<Guid>(), null), default));
        var usage = await Call(s => s.Membership.GetMembershipUsage(cid, member.ResourceId, readOnly, default));
        if (usage.MembershipId != member.ResourceId || !usage.IsActive || usage.HasReferences ||
            usage.AssignedRoleCount != 0 || usage.WarehouseKeeperCount != 0)
            throw new Exception("SYS06 SQL usage counted wrong member or unrelated references");
        try
        {
            await Call(s => s.Membership.ListMembershipAudit(cid, member.ResourceId, readOnly, 0, 10, default));
            throw new Exception("SYS06 read-only member admin viewed privileged audit");
        }
        catch (BusinessException ex) when (ex.Code == "permission_denied") { }
        var inactive = await Call(s => s.Membership.SetMembershipStatus(cid, member.ResourceId, owner,
            Fixture.Key(), new(member.Version, false, "تعطيل"), default));
        int beforeAudit, beforeReceipts, beforeOutbox;
        await using (var db = Context())
        {
            beforeAudit = await db.Set<AuditEvent>().CountAsync();
            beforeReceipts = await db.Set<IdempotencyRecord>().CountAsync();
            beforeOutbox = await db.Set<OutboxEvent>().CountAsync();
        }
        var disabled = await Call(s => s.Membership.GetMembershipUsage(cid, member.ResourceId, owner, default));
        var audit = await Call(s => s.Membership.ListMembershipAudit(cid, member.ResourceId, owner, 0, 10, default));
        var firstPage = await Call(s => s.Membership.ListMembershipAudit(cid, member.ResourceId, owner, 0, 1, default));
        if (disabled.IsActive || disabled.Subject != readOnly.Subject || audit.TotalCount != 2 ||
            firstPage.TotalCount != 2 || firstPage.Items.Count != 1 ||
            audit.Items.Any(x => x.ResourceId != member.ResourceId ||
                (x.Action != "membership.granted" && x.Action != "membership.status-changed")))
            throw new Exception("SYS06 SQL disabled membership usage/audit paging differs");
        await using (var db = Context())
        {
            if (await db.Set<AuditEvent>().CountAsync() != beforeAudit ||
                await db.Set<IdempotencyRecord>().CountAsync() != beforeReceipts ||
                await db.Set<OutboxEvent>().CountAsync() != beforeOutbox ||
                !(await db.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Id == member.ResourceId)).Version.Equals(inactive.Version))
                throw new Exception("SYS06 SQL usage/audit reads altered domain history");
        }
        Console.WriteLine("  SQL PASS SYS06 usage/audit tenant-scoped, disabled history, permissions and read-only idempotency");
    }
}
