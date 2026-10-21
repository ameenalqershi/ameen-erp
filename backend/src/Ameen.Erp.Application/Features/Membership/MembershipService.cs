using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    public Task<CommandReceipt> GrantMembership(Guid cid, Actor actor, string key, GrantMembershipRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "membership.grant", request, token => AuthorizeGlobal(cid, actor, Permissions.Members, token), async token =>
        {
            Check.That(request.Permissions is not null && request.BranchIds is not null, "invalid_membership", "الصلاحيات والفروع مطلوبة.");
            var subject = Check.Text(request.Subject, "معرف المستخدم");
            Check.That(subject != actor.Subject, "self_grant_forbidden", "لا يمكنك تغيير صلاحياتك بنفسك.", ErrorKind.Forbidden);
            foreach (var branchId in request.BranchIds!) await ActiveBranch(cid, branchId, token);
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

    public async Task<Page<Membership>> ListMembers(Guid cid, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Members, ct);
        return await store.ReadPage<Membership>(cid, x => true, q => q.OrderBy(x => x.Subject).ThenBy(x => x.Id), offset, limit, ct);
    }
}
