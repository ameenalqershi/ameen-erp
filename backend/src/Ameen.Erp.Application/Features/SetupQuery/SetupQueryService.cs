using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class SetupQueryService(IErpStore store, ITransactionRunner transactions, IClock clock) : ApplicationService(store, transactions, clock)
{
    public async Task<IReadOnlyList<T>> ListSetup<T>(Guid cid, Actor actor, CancellationToken ct) where T : CompanyEntity
    {
        // This API is only exposed for non-sensitive master data, never arbitrary entity types.
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        var result = await store.Find<T>(cid, x => true, ct);
        if (typeof(T) == typeof(CostCenter))
            return result.Cast<CostCenter>().Where(x => x.BranchId is null || member.Allows(x.BranchId.Value)).Cast<T>().ToArray();
        return result;
    }
}
