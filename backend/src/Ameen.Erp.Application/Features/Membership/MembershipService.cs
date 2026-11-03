using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class MembershipService : ApplicationService
{
    private readonly BranchService branchService;
    public MembershipService(IErpStore store, ITransactionRunner transactions, IClock clock, BranchService branchService) : base(store, transactions, clock)
    {
        this.branchService = branchService;
    }


    public Task<CommandReceipt> GrantMembership(Guid cid, Actor actor, string key, GrantMembershipRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "membership.grant", request, token => AuthorizeGlobal(cid, actor, Permissions.Members, token), async token =>
        {
            Check.That(request.Permissions is not null && request.BranchIds is not null, "invalid_membership", "الصلاحيات والفروع مطلوبة.");
            var subject = Check.Text(request.Subject, "معرف المستخدم");
            Check.That(subject != actor.Subject, "self_grant_forbidden", "لا يمكنك تغيير صلاحياتك بنفسك.", ErrorKind.Forbidden);
            foreach (var branchId in request.BranchIds!) await branchService.ActiveBranch(cid, branchId, token);
            var member = (await store.Find<Membership>(cid, x => x.Subject == subject, token)).SingleOrDefault();
            var before = member is null ? null : new { member.GrantedPermissions, member.AllBranches, member.BranchIds, member.Version };
            if (member is null)
            {
                Check.That(request.ExpectedVersion is null, "membership_missing", "العضوية المطلوب تعديلها غير موجودة.", ErrorKind.Conflict);
                member = new Membership(cid, subject, request.Permissions!, request.AllBranches, request.BranchIds!);
                store.Add(member);
            }
            else
            {
                Check.Version(member, request.ExpectedVersion ?? Guid.Empty);
                member.Configure(request.Permissions!, request.AllBranches, request.BranchIds!);
            }
            Audit(cid, actor, "membership.granted", member, new { subject, before, after = new { member.GrantedPermissions, member.AllBranches, member.BranchIds } });
            return Receipt(member, "Granted");
        }, ct);

    public Task<CommandReceipt> SetMembershipStatus(Guid cid, Guid id, Actor actor, string key, SetMembershipStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "membership.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Members, token), async token =>
        {
            var member = await Required<Membership>(cid, id, token); Check.Version(member, request.ExpectedVersion);
            Check.That(member.Subject != actor.Subject, "self_grant_forbidden", "لا يمكنك تغيير حالة عضويتك بنفسك.", ErrorKind.Forbidden);
            var reason = Check.Text(request.Reason, "السبب", 500); var before = member.IsActive;
            member.SetActive(request.IsActive);
            Audit(cid, actor, "membership.status-changed", member, new { before, after = member.IsActive, reason });
            return Receipt(member, member.IsActive ? "Active" : "Inactive");
        }, ct);

    public async Task<MembershipUsage> GetMembershipUsage(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeGlobal(cid, actor, Permissions.Members, ct);
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        var member = await Required<Membership>(cid, id, ct);
        var roles = (await store.Find<SecurityRoleAssignment>(cid, x => x.MembershipId == id, ct)).Count;
        var warehouses = (await store.Find<Warehouse>(cid, x => x.KeeperMembershipId == id, ct)).Count;
        var costCenters = (await store.Find<CostCenter>(cid, x => x.ResponsibleMembershipId == id, ct)).Count;
        var journals = (await store.Find<JournalEntry>(cid, x => x.CreatedBy == member.Subject, ct)).Count;
        var templates = (await store.Find<RecurringJournalTemplate>(cid, x => x.CreatedBy == member.Subject, ct)).Count;
        return new(id, member.Subject, member.IsActive, roles, warehouses, costCenters, journals, templates,
            roles + warehouses + costCenters + journals + templates > 0);
    }

    public async Task<Page<AuditEvent>> ListMembershipAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        await AuthorizeGlobal(cid, actor, Permissions.Members, ct);
        await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        await Required<Membership>(cid, id, ct);
        return await store.ReadPage<AuditEvent>(cid,
            x => x.ResourceId == id && (x.Action == "membership.granted" || x.Action == "membership.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }

    public async Task<Page<Membership>> ListMembers(Guid cid, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Members, ct);
        return await store.ReadPage<Membership>(cid, x => true, q => q.OrderBy(x => x.Subject).ThenBy(x => x.Id), offset, limit, ct);
    }
}
