using System.Linq.Expressions;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed class SqlErpStore : IErpStore
{
    private readonly ErpDbContext db;
    public SqlErpStore(ErpDbContext db) { this.db = db; }
    public Task<Company?> GetCompany(Guid id, CancellationToken ct) => db.Set<Company>().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> CompanyCodeExists(string code, CancellationToken ct) => db.Set<Company>().AnyAsync(x => x.Code == code, ct);
    public async Task<IReadOnlyList<Company>> CompaniesFor(string subject, CancellationToken ct) =>
        await db.Set<Company>().AsNoTracking().Where(c => db.Set<Membership>().Any(m => m.CompanyId == c.Id && m.Subject == subject && m.IsActive))
            .OrderBy(c => c.Code).ToListAsync(ct);
    public Task<T?> Get<T>(Guid companyId, Guid id, CancellationToken ct) where T : CompanyEntity =>
        db.Set<T>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
    public async Task<IReadOnlyList<T>> Find<T>(Guid companyId, Expression<Func<T, bool>> predicate, CancellationToken ct) where T : CompanyEntity =>
        await db.Set<T>().Where(x => x.CompanyId == companyId).Where(predicate).ToListAsync(ct);
    public void Add(Entity entity) => db.Add(entity);
    public async Task<Page<T>> ReadPage<T>(Guid companyId, Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> order, int offset, int limit, CancellationToken ct) where T : CompanyEntity
    {
        var query = db.Set<T>().AsNoTracking().Where(x => x.CompanyId == companyId).Where(predicate);
        var count = await query.CountAsync(ct);
        var items = await order(query).Skip(offset).Take(limit).ToListAsync(ct);
        return new(items, count, offset, limit);
    }
    public void RemoveLines(IEnumerable<JournalLine> lines) => db.RemoveRange(lines);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
