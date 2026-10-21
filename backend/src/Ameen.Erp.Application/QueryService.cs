using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private static void Paging(int offset, int limit)
    {
        Check.That(offset >= 0 && offset <= 1_000_000 && limit is >= 1 and <= 200,
            "invalid_pagination", "حجم الصفحة من 1 إلى 200 وإزاحة صالحة مطلوبان.");
    }
    public Task<IReadOnlyList<Company>> ListCompanies(Actor actor, CancellationToken ct) => store.CompaniesFor(actor.Subject, ct);
    public async Task<Company> GetCompany(Guid cid, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct); return await Company(cid, ct);
    }
    public async Task<IReadOnlyList<Branch>> ListBranches(Guid cid, Actor actor, CancellationToken ct)
    {
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        var branches = await store.Find<Branch>(cid, x => true, ct);
        return branches.Where(x => member.Allows(x.Id)).OrderBy(x => x.Code).ToArray();
    }
    public async Task<IReadOnlyList<T>> ListSetup<T>(Guid cid, Actor actor, CancellationToken ct) where T : CompanyEntity
    {
        // This API is only exposed for non-sensitive master data, never arbitrary entity types.
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        var result = await store.Find<T>(cid, x => true, ct);
        if (typeof(T) == typeof(CostCenter))
            return result.Cast<CostCenter>().Where(x => x.BranchId is null || member.Allows(x.BranchId.Value)).Cast<T>().ToArray();
        return result;
    }
    public async Task<Page<Account>> ListAccounts(Guid cid, Actor actor, string? search, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح.");
        search = search?.Trim();
        return await store.ReadPage<Account>(cid, x => search == null || x.Code.Contains(search) || x.Name.Contains(search),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<JournalEntry> GetJournal(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeJournal(cid, id, actor, Permissions.Read, ct); return await Required<JournalEntry>(cid, id, ct);
    }
    public async Task<Page<JournalSummary>> ListJournals(Guid cid, Actor actor, Guid? branchId,
        JournalState? state, int offset, int limit, CancellationToken ct, JournalSource? source = null)
    {
        Paging(offset, limit);
        if (state is not null) Check.That(Enum.IsDefined(typeof(JournalState), state.Value), "invalid_state", "حالة القيد غير معروفة.");
        if (source is not null) Check.That(Enum.IsDefined(source.Value), "invalid_source", "مصدر القيد غير معروف.");
        var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        var branches = await AllowedBranches(cid, member, branchId, ct);
        var page = await store.ReadPage<JournalEntry>(cid, x => branches.Contains(x.BranchId) && (state == null || x.State == state) && (source == null || x.Source == source),
            q => q.OrderByDescending(x => x.AccountingDate).ThenByDescending(x => x.Sequence).ThenBy(x => x.Id), offset, limit, ct);
        return new(page.Items.Select(x => new JournalSummary(x.Id, x.Version, x.Number, x.BranchId, x.AccountingDate,
                x.Description, x.State, x.Lines.Sum(l => l.FunctionalDebit), x.Lines.Sum(l => l.FunctionalCredit), x.Source, x.RecurringTemplateId)).ToArray(), page.TotalCount, offset, limit);
    }
    private async Task<Guid[]> AllowedBranches(Guid cid, Membership member, Guid? branchId, CancellationToken ct)
    {
        if (branchId is Guid id)
        {
            await Required<Branch>(cid, id, ct); return new[] { id };
        }
        return (await store.Find<Branch>(cid, x => true, ct)).Where(x => member.Allows(x.Id)).Select(x => x.Id).ToArray();
    }
    public async Task<Page<Membership>> ListMembers(Guid cid, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Members, ct);
        return await store.ReadPage<Membership>(cid, x => true, q => q.OrderBy(x => x.Subject).ThenBy(x => x.Id), offset, limit, ct);
    }
    public async Task<Page<AuditEvent>> ListAudit(Guid cid, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        return await store.ReadPage<AuditEvent>(cid, x => true, q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    private async Task ValidateReportCenter(Guid cid, Membership member, Guid? branchId, Guid? centerId, CancellationToken ct)
    {
        if (centerId is not Guid id) return;
        var center = await Required<CostCenter>(cid, id, ct);
        Check.That(center.BranchId is null || member.Allows(center.BranchId.Value),
            "branch_access_denied", "مركز التكلفة خارج نطاق الفروع المسموحة.", ErrorKind.Forbidden);
        Check.That(center.BranchId is null || branchId is null || center.BranchId == branchId,
            "cost_center_mismatch", "مركز التكلفة تابع لفرع آخر.");
    }
    public async Task<TrialBalanceResult> TrialBalance(Guid cid, Actor actor, DateOnly from, DateOnly to, Guid? branchId,
        CancellationToken ct, Guid? costCenterId = null, int? accountLevel = null, bool showZero = true)
    {
        Check.That(to >= from, "invalid_date_range", "نهاية التقرير قبل بدايته.");
        Check.That(accountLevel is null or >= 1 and <= 20, "invalid_account_level", "مستوى دليل الحسابات بين 1 و20.");
        var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        await ValidateReportCenter(cid, member, branchId, costCenterId, ct);
        var company = await Company(cid, ct); var branches = await AllowedBranches(cid, member, branchId, ct);
        var journals = await store.Find<JournalEntry>(cid, x => branches.Contains(x.BranchId) && x.AccountingDate <= to &&
            (x.State == JournalState.Posted || x.State == JournalState.Reversed), ct);
        var accounts = await store.Find<Account>(cid, x => true, ct);
        var byId = accounts.ToDictionary(x => x.Id);
        var paths = new Dictionary<Guid, Account[]>();
        foreach (var account in accounts)
        {
            var path = new List<Account>(); var visited = new HashSet<Guid>(); var current = account;
            while (true)
            {
                Check.That(visited.Add(current.Id), "invalid_account_tree", "توجد دورة في شجرة الحسابات.", ErrorKind.Conflict);
                path.Add(current);
                if (current.ParentId is not Guid parent) break;
                Check.That(byId.ContainsKey(parent), "invalid_account_tree", "الحساب الأب غير موجود ضمن الشركة.", ErrorKind.Conflict);
                current = byId[parent];
            }
            path.Reverse(); paths[account.Id] = path.ToArray();
        }
        Account Target(Guid id) => accountLevel is int level ? paths[id][Math.Min(level, paths[id].Length) - 1] : byId[id];
        var displayAccounts = accounts.Where(x => x.IsPosting).Select(x => Target(x.Id)).DistinctBy(x => x.Id);
        var lines = journals.SelectMany(j => j.Lines.Where(l => costCenterId == null || l.CostCenterId == costCenterId)
            .Select(l => (Date: j.AccountingDate, Line: l))).ToLookup(x => Target(x.Line.AccountId).Id);
        var rows = displayAccounts.OrderBy(x => x.Code, StringComparer.Ordinal).Select(a =>
        {
            var items = lines[a.Id].ToArray();
            var opening = items.Where(x => x.Date < from).Sum(x => x.Line.FunctionalDebit - x.Line.FunctionalCredit);
            var debit = items.Where(x => x.Date >= from).Sum(x => x.Line.FunctionalDebit);
            var credit = items.Where(x => x.Date >= from).Sum(x => x.Line.FunctionalCredit);
            var closing = opening + debit - credit;
            return new TrialBalanceRow(a.Id, a.Code, a.Name, Math.Max(opening, 0), Math.Max(-opening, 0), debit, credit,
                Math.Max(closing, 0), Math.Max(-closing, 0), a.ParentId, paths[a.Id].Length, a.IsPosting);
        }).Where(x => showZero || x.OpeningDebit != 0 || x.OpeningCredit != 0 || x.PeriodDebit != 0 || x.PeriodCredit != 0).ToArray();
        return new(cid, company.FunctionalCurrency, from, to, branches, rows,
            rows.Sum(x => x.OpeningDebit), rows.Sum(x => x.OpeningCredit), rows.Sum(x => x.PeriodDebit),
            rows.Sum(x => x.PeriodCredit), rows.Sum(x => x.ClosingDebit), rows.Sum(x => x.ClosingCredit), costCenterId, accountLevel, showZero);
    }
    public async Task<AccountLedgerResult> AccountLedger(Guid cid, Guid accountId, Actor actor, DateOnly from,
        DateOnly to, Guid? branchId, int offset, int limit, CancellationToken ct, Guid? costCenterId = null, string? externalReference = null)
    {
        Paging(offset, limit); Check.That(to >= from, "invalid_date_range", "نهاية التقرير قبل بدايته.");
        var member = await Authorize(cid, actor, Permissions.Read, branchId, ct);
        await ValidateReportCenter(cid, member, branchId, costCenterId, ct);
        externalReference = string.IsNullOrWhiteSpace(externalReference) ? null : Check.Text(externalReference, "المرجع الخارجي", 100);
        await Required<Account>(cid, accountId, ct);
        var company = await Company(cid, ct); var branches = await AllowedBranches(cid, member, branchId, ct);
        var journals = await store.Find<JournalEntry>(cid, x => branches.Contains(x.BranchId) && x.AccountingDate <= to &&
            (x.State == JournalState.Posted || x.State == JournalState.Reversed) &&
            (externalReference == null || x.ExternalReference == externalReference) &&
            x.Lines.Any(l => l.AccountId == accountId && (costCenterId == null || l.CostCenterId == costCenterId)), ct);
        var items = journals.OrderBy(x => x.AccountingDate).ThenBy(x => x.Sequence).ThenBy(x => x.Id)
            .SelectMany(j => j.Lines.Where(l => l.AccountId == accountId && (costCenterId == null || l.CostCenterId == costCenterId))
                .OrderBy(l => l.Position).Select(l => (Journal: j, Line: l))).ToArray();
        var opening = items.Where(x => x.Journal.AccountingDate < from).Sum(x => x.Line.FunctionalDebit - x.Line.FunctionalCredit);
        var running = opening; var rows = new List<LedgerRow>();
        foreach (var (journal, line) in items.Where(x => x.Journal.AccountingDate >= from))
        {
            running += line.FunctionalDebit - line.FunctionalCredit;
            rows.Add(new(journal.Id, journal.Number, journal.AccountingDate, line.Position, line.Description,
                line.FunctionalDebit, line.FunctionalCredit, running, line.CurrencyCode, line.Debit, line.Credit, line.ExchangeRate));
        }
        return new(accountId, company.FunctionalCurrency, from, to, opening, running,
            rows.Sum(x => x.Debit), rows.Sum(x => x.Credit), new(rows.Skip(offset).Take(limit).ToArray(), rows.Count, offset, limit),
            cid, branches, costCenterId, externalReference);
    }
}
