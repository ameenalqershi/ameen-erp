using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// SQL integration ONLY: test persisted structural headers/lines from the
// disposable database. Unapproved financial workflows remain fail-closed.
// Unlike a rejected insert, this checks that tracked UPDATE and DELETE of
// already persisted records cannot bypass the EF write gate.
public static class CashUnauthorizedEfMutationSqlIntegration
{
    public static async Task RejectUpdateAndDelete<T>(
        ErpDbContext db, T existing, string errorCode)
        where T : CompanyEntity
    {
        var auditBefore = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == existing.CompanyId);
        var outboxBefore = await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == existing.CompanyId);
        var journalBefore = await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == existing.CompanyId);
        var originalVersion = existing.Version;

        foreach (var operation in new[] { EntityState.Modified, EntityState.Deleted })
        {
            // The SQL fixtures were read AsNoTracking: no other instance of this
            // record may be attached and accidentally pass a no-op test.
            var entry = db.Entry(existing);
            entry.State = operation;
            try
            {
                // EF validation can throw synchronously while constructing this
                // Task, so invocation must occur inside the try.
                await db.SaveChangesAsync();
                throw new Exception($"Unauthorized CASH EF {operation} accepted for {typeof(T).Name}");
            }
            catch (BusinessException e) when (e.Code == errorCode)
            {
                // Expected: fail-closed and no mutation of any tracked entity.
            }
            finally
            {
                entry.State = EntityState.Detached;
            }

            var after = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == existing.CompanyId && x.Id == existing.Id);
            if (after is null || after.Version != originalVersion ||
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == existing.CompanyId) != auditBefore ||
                await db.Set<OutboxEvent>().CountAsync(x => x.CompanyId == existing.CompanyId) != outboxBefore ||
                await db.Set<JournalEntry>().CountAsync(x => x.CompanyId == existing.CompanyId) != journalBefore)
            {
                throw new Exception($"Rejected CASH EF {operation} changed persisted {typeof(T).Name} or accounting/audit/outbox");
            }
        }
        Console.WriteLine($"  SQL PASS CASH fail-closed EF UPDATE/DELETE: {typeof(T).Name}");
    }
}
