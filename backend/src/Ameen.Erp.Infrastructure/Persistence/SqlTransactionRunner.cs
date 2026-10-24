using System.Data;
using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed class SqlTransactionRunner : ITransactionRunner
{
    private readonly ErpDbContext db;
    private readonly IClock clock;
    public SqlTransactionRunner(ErpDbContext db, IClock clock) { this.db = db; this.clock = clock; }

    public async Task<CommandReceipt> Run(CommandIdentity identity, Func<CancellationToken, Task> authorize,
        Func<CancellationToken, Task<CommandReceipt>> work, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        async Task Rollback()
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            // SQL Server can already have rolled back a deadlock victim or lost connection.
            catch (InvalidOperationException) { }
            catch (SqlException) { }
            finally { db.ChangeTracker.Clear(); }
        }
        try
        {
            // Deliberately coarse company write lock: correctness before unmeasured parallel throughput.
            // All financial mutations, period changes and grants use this same transaction-owned lock.
            var resource = "ameen:company:" + identity.CompanyId.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
IF @result < 0 THROW 51000, 'Company write lock unavailable', 1;", ct);
            // A scoped service may execute several independent batch commands. Discard prior tracked
            // reads after acquiring the lock so grants, periods and versions are current for each item.
            db.ChangeTracker.Clear();
            // Re-authorize even idempotent replays, including current branch scope.
            await authorize(ct);
            var previous = await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(
                x => x.CompanyId == identity.CompanyId && x.Key == identity.Key, ct);
            if (previous is not null)
            {
                Check.That(previous.Operation == identity.Operation && previous.ActorId == identity.ActorId && previous.Fingerprint == identity.Fingerprint,
                    "idempotency_conflict", "استُخدم مفتاح الطلب مع محتوى أو عملية أو مستخدم مختلف.", ErrorKind.Conflict);
                var receipt = JsonSerializer.Deserialize<CommandReceipt>(previous.ReceiptJson, JsonDefaults.Options)!;
                await transaction.CommitAsync(ct); return receipt;
            }
            var result = await work(ct);
            db.Add(new IdempotencyRecord(identity.CompanyId, identity.Key, identity.Operation, identity.ActorId,
                identity.Fingerprint, JsonSerializer.Serialize(result, JsonDefaults.Options), clock.UtcNow));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await Rollback();
            throw new BusinessException("version_conflict", "تغير السجل بواسطة عملية أخرى؛ أعد تحميله.", ErrorKind.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 547)
        {
            await Rollback();
            throw new BusinessException("database_constraint", "تعارض في تفرد البيانات أو ارتباطاتها؛ لم تُحفظ العملية.", ErrorKind.Conflict);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 51000)
        {
            await Rollback();
            throw new BusinessException("write_conflict", "توجد عملية متزامنة؛ أعد المحاولة بالمفتاح نفسه.", ErrorKind.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 1205 or 51000)
        {
            await Rollback();
            throw new BusinessException("write_conflict", "توجد عملية متزامنة؛ أعد المحاولة بالمفتاح نفسه.", ErrorKind.Conflict);
        }
        catch
        {
            await Rollback(); throw;
        }
    }
}
