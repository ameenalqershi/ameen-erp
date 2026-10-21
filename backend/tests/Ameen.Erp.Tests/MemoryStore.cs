using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// Test double only: production always uses SqlErpStore and SqlTransactionRunner.
public sealed class MemoryStore : IErpStore, ITransactionRunner
{
    private List<Entity> entities = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool FailBeforeCommit { get; set; }
    public int Count<T>() where T : Entity => entities.OfType<T>().Count();
    public IEnumerable<T> All<T>() where T : Entity => entities.OfType<T>();
    public Task<Company?> GetCompany(Guid id, CancellationToken ct) => Task.FromResult(entities.OfType<Company>().SingleOrDefault(x => x.Id == id));
    public Task<bool> CompanyCodeExists(string code, CancellationToken ct) => Task.FromResult(entities.OfType<Company>().Any(x => x.Code == code));
    public Task<IReadOnlyList<Company>> CompaniesFor(string subject, CancellationToken ct) => Task.FromResult<IReadOnlyList<Company>>(
        entities.OfType<Company>().Where(c => entities.OfType<Membership>().Any(m => m.CompanyId == c.Id && m.Subject == subject && m.IsActive)).ToArray());
    public Task<T?> Get<T>(Guid cid, Guid id, CancellationToken ct) where T : CompanyEntity =>
        Task.FromResult(entities.OfType<T>().SingleOrDefault(x => x.CompanyId == cid && x.Id == id));
    public Task<IReadOnlyList<T>> Find<T>(Guid cid, Expression<Func<T, bool>> predicate, CancellationToken ct) where T : CompanyEntity =>
        Task.FromResult<IReadOnlyList<T>>(entities.OfType<T>().Where(x => x.CompanyId == cid).Where(predicate.Compile()).ToArray());
    public void Add(Entity entity) => entities.Add(entity);
    public Task<Page<T>> ReadPage<T>(Guid companyId, Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IOrderedQueryable<T>> order, int offset, int limit, CancellationToken ct) where T : CompanyEntity
    {
        var query = entities.OfType<T>().Where(x => x.CompanyId == companyId).AsQueryable().Where(predicate);
        return Task.FromResult(new Page<T>(order(query).Skip(offset).Take(limit).ToArray(), query.Count(), offset, limit));
    }
    public void RemoveLines(IEnumerable<JournalLine> lines) { /* Lines are owned by the test aggregate. */ }
    public void RemoveWarehouseLocations(IEnumerable<WarehouseLocation> locations) { /* Owned by warehouse aggregate. */ }
    public void RemoveItemUnits(IEnumerable<ItemUnit> units) { /* Owned by the item aggregate. */ }
    public void RemoveRecurringLines(IEnumerable<RecurringJournalLine> lines) { /* Lines are owned by the test aggregate. */ }
    public Task Save(CancellationToken ct) => Task.CompletedTask;

    public async Task<CommandReceipt> Run(CommandIdentity identity, Func<CancellationToken, Task> authorize,
        Func<CancellationToken, Task<CommandReceipt>> work, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        var snapshot = entities.Select(x => (Entity)Clone(x)).ToList();
        try
        {
            await authorize(ct);
            var previous = entities.OfType<IdempotencyRecord>().SingleOrDefault(x => x.CompanyId == identity.CompanyId && x.Key == identity.Key);
            if (previous is not null)
            {
                Check.That(previous.Operation == identity.Operation && previous.ActorId == identity.ActorId && previous.Fingerprint == identity.Fingerprint,
                    "idempotency_conflict", "Conflict", ErrorKind.Conflict);
                return JsonSerializer.Deserialize<CommandReceipt>(previous.ReceiptJson, JsonDefaults.Options)!;
            }
            var result = await work(ct);
            if (FailBeforeCommit) { FailBeforeCommit = false; throw new IOException("Injected persistence failure"); }
            entities.Add(new IdempotencyRecord(identity.CompanyId, identity.Key, identity.Operation, identity.ActorId,
                identity.Fingerprint, JsonSerializer.Serialize(result, JsonDefaults.Options), DateTimeOffset.UtcNow));
            return result;
        }
        catch { entities = snapshot; throw; }
        finally { gate.Release(); }
    }

    private static object Clone(object value)
    {
        if (value is string || value.GetType().IsValueType) return value;
        if (value is Array a)
        {
            var copy = (Array)a.Clone();
            for (var i = 0; i < copy.Length; i++) if (a.GetValue(i) is object item) copy.SetValue(Clone(item), i);
            return copy;
        }
        if (value is IList list)
        {
            var result = (IList)Activator.CreateInstance(value.GetType())!;
            foreach (var item in list) result.Add(Clone(item!));
            return result;
        }
        var clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
        for (var type = value.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            if (field.GetValue(value) is object child && !field.IsInitOnly) field.SetValue(clone, Clone(child));
        return clone;
    }
}

public sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow => new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
}

public sealed class Fixture
{
    public MemoryStore Store { get; } = new();
    public ErpService Service { get; }
    public Guid CompanyId { get; } = Guid.NewGuid();
    public Actor Owner { get; } = new("owner", true);
    public Actor Clerk { get; } = new("clerk");
    public Actor Approver { get; } = new("approver");
    public Actor Limited { get; } = new("branch-user");
    public Guid BranchId { get; private set; }
    public Guid OtherBranchId { get; private set; }
    public Guid CashId { get; private set; }
    public Guid EquityId { get; private set; }
    public Guid GroupId { get; private set; }
    public static readonly DateOnly Date = new(2026, 10, 20);
    public static CompanyProfile Profile() => new("شركة الاختبار القانونية", "منطقة الاختبار", new(2026, 1, 1), "مدينة الاختبار", "شارع الاختبار");
    public static string Key() => Guid.NewGuid().ToString("N");
    public Fixture() { Service = new ErpService(Store, Store, new FixedClock()); }
    public async Task Initialize()
    {
        await Service.CreateCompany(Owner, Key(), new(CompanyId, "TEST", "شركة الاختبار", "YE", "SAR", 2, Fixture.Profile()), default);
        BranchId = (await Service.CreateBranch(CompanyId, Owner, Key(), new("MAIN", "الرئيسي", "مدينة الاختبار", "عنوان الاختبار"), default)).ResourceId;
        OtherBranchId = (await Service.CreateBranch(CompanyId, Owner, Key(), new("OTHER", "الآخر", "مدينة الاختبار", "عنوان الاختبار"), default)).ResourceId;
        await Service.CreateFiscalYear(CompanyId, Owner, Key(), new("2026", new(2026, 1, 1), new(2026, 12, 31)), default);
        await Service.GrantMembership(CompanyId, Owner, Key(), new(Clerk.Subject,
            new[] { Permissions.Read, Permissions.Draft, Permissions.Approve, Permissions.Post, Permissions.Reverse }, true, Array.Empty<Guid>(), null), default);
        await Service.GrantMembership(CompanyId, Owner, Key(), new(Approver.Subject,
            new[] { Permissions.Read, Permissions.Approve, Permissions.Post, Permissions.PeriodClose, Permissions.PeriodReopen, Permissions.Audit }, true, Array.Empty<Guid>(), null), default);
        await Service.GrantMembership(CompanyId, Owner, Key(), new(Limited.Subject,
            new[] { Permissions.Read, Permissions.Draft, Permissions.Post }, false, new[] { BranchId }, null), default);
        GroupId = (await Service.CreateAccount(CompanyId, Owner, Key(), new("1000", "الأصول", AccountType.Asset, null, false, false, false, null, NormalBalance.Debit), default)).ResourceId;
        CashId = (await Service.CreateAccount(CompanyId, Owner, Key(), new("1101", "نقدية", AccountType.Asset, GroupId, true, false, false, null, NormalBalance.Debit), default)).ResourceId;
        EquityId = (await Service.CreateAccount(CompanyId, Owner, Key(), new("3001", "رأس المال", AccountType.Equity, null, true, false, false, null, NormalBalance.Credit), default)).ResourceId;
    }
    public CreateJournalRequest Request(decimal debit = 100, decimal credit = 100, Guid? branch = null, DateOnly? date = null) =>
        new(branch ?? BranchId, date ?? Date, date ?? Date, "قيد اختبار", null,
            new[] { new JournalLineRequest(CashId, null, "الطرف المدين", "SAR", null, debit, 0),
                new JournalLineRequest(EquityId, null, "الطرف الدائن", "SAR", null, 0, credit) });
    public Task<CommandReceipt> Draft(CreateJournalRequest? request = null, string? key = null, Actor? actor = null) =>
        Service.CreateJournal(CompanyId, actor ?? Clerk, key ?? Key(), request ?? Request(), default);
    public Task<JournalEntry> Journal(Guid id) => Service.GetJournal(CompanyId, id, Clerk, default);
    public Task<CommandReceipt> Action(CommandReceipt r, string action, Actor? actor = null, string? key = null, string? reason = null) =>
        Service.TransitionJournal(CompanyId, r.ResourceId, actor ?? Clerk, key ?? Key(), new(r.Version, reason), action, default);
    public async Task<CommandReceipt> Post(CommandReceipt? draft = null)
    {
        var r = await Action(draft ?? await Draft(), "submit");
        r = await Action(r, "approve", Approver);
        return await Action(r, "post");
    }
}
