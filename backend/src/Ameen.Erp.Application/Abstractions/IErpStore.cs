using System.Linq.Expressions;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public interface IErpStore
{
    Task<Company?> GetCompany(Guid id, CancellationToken ct);
    Task<bool> CompanyCodeExists(string code, CancellationToken ct);
    Task<IReadOnlyList<Company>> CompaniesFor(string subject, CancellationToken ct);
    Task<T?> Get<T>(Guid companyId, Guid id, CancellationToken ct) where T : CompanyEntity;
    Task<IReadOnlyList<T>> Find<T>(Guid companyId, Expression<Func<T, bool>> predicate, CancellationToken ct) where T : CompanyEntity;
    Task<Page<T>> ReadPage<T>(Guid companyId, Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> order, int offset, int limit, CancellationToken ct) where T : CompanyEntity;
    Task<Page<AuditEvent>> ReadAuditPage(Guid companyId, AuditReadFilter filter, int offset, int limit, CancellationToken ct);
    void Add(Entity entity);
    void RemoveInventoryOpeningLines(IEnumerable<InventoryOpeningLine> lines);
    void RemoveFinancialMappingLines(IEnumerable<FinancialMappingLine> lines);
    void RemoveLines(IEnumerable<JournalLine> lines);
    void RemoveWarehouseLocations(IEnumerable<WarehouseLocation> locations);
    void RemoveItemUnits(IEnumerable<ItemUnit> units);
    void RemoveRecurringLines(IEnumerable<RecurringJournalLine> lines);
    Task Save(CancellationToken ct);
}
