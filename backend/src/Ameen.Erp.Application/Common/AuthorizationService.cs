using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private async Task<Membership> Authorize(Guid companyId, Actor actor, string permission, Guid? branchId, CancellationToken ct)
    {
        var membership = (await store.Find<Membership>(companyId, x => x.Subject == actor.Subject, ct)).SingleOrDefault();
        Check.That(membership is not null && membership.Has(permission), "permission_denied", "لا تملك صلاحية العملية ضمن هذه الشركة.", ErrorKind.Forbidden);
        Check.That(branchId is null || membership!.Allows(branchId.Value), "branch_access_denied", "الفرع خارج نطاق صلاحياتك.", ErrorKind.Forbidden);
        return membership!;
    }

    private async Task AuthorizeGlobal(Guid companyId, Actor actor, string permission, CancellationToken ct)
    {
        var membership = await Authorize(companyId, actor, permission, null, ct);
        Check.That(membership.AllBranches, "all_branches_required", "هذه العملية تتطلب صلاحية على جميع فروع الشركة.", ErrorKind.Forbidden);
    }

    private async Task AuthorizeJournal(Guid companyId, Guid journalId, Actor actor, string permission, CancellationToken ct)
    {
        await Authorize(companyId, actor, permission, null, ct);
        var journal = await Required<JournalEntry>(companyId, journalId, ct);
        await Authorize(companyId, actor, permission, journal.BranchId, ct);
    }

    private async Task<Guid[]> AllowedBranches(Guid cid, Membership member, Guid? branchId, CancellationToken ct)
    {
        if (branchId is Guid id)
        {
            await Required<Branch>(cid, id, ct); return new[] { id };
        }
        return (await store.Find<Branch>(cid, x => true, ct)).Where(x => member.Allows(x.Id)).Select(x => x.Id).ToArray();
    }
}
