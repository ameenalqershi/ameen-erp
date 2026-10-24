using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class AccountService : ApplicationService
{
    private readonly CurrencyService currencyService;
    public AccountService(IErpStore store, ITransactionRunner transactions, IClock clock, CurrencyService currencyService) : base(store, transactions, clock)
    {
        this.currencyService = currencyService;
    }


    private static object AccountSnapshot(Account x) => new
    {
        x.Version, x.Revision, x.Code, x.Name, x.EnglishName, x.Notes, x.Type, x.ParentId, x.NormalBalance,
        x.IsPosting, x.IsControl, x.RequiresCostCenter, x.CurrencyCode, x.IsActive
    };

    private static Guid[] AccountSubtree(Guid id, IReadOnlyList<Account> accounts)
    {
        var children = accounts.Where(x => x.ParentId is not null).ToLookup(x => x.ParentId!.Value);
        var pending = new Stack<Guid>(); pending.Push(id); var visited = new HashSet<Guid>();
        while (pending.TryPop(out var current))
        {
            Check.That(visited.Add(current), "invalid_account_tree", "توجد دورة في شجرة الحسابات.", ErrorKind.Conflict);
            foreach (var child in children[current]) pending.Push(child.Id);
        }
        return visited.ToArray();
    }

    private async Task<bool> HasAccountUsage(Guid cid, Guid[] ids, CancellationToken ct) =>
        (await store.Find<InventoryOpeningPostingSettings>(cid, x => ids.Contains(x.ClearingAccountId), ct)).Count > 0 ||
        (await store.Find<Warehouse>(cid, x => ids.Contains(x.InventoryAccountId), ct)).Count > 0 ||
        (await store.Find<JournalEntry>(cid, x => x.Lines.Any(l => ids.Contains(l.AccountId)), ct)).Count > 0 ||
        (await store.Find<RecurringJournalTemplate>(cid, x => x.Lines.Any(l => ids.Contains(l.AccountId)), ct)).Count > 0 ||
        (await store.Find<ItemGroup>(cid, x => ids.Contains(x.SalesAccountId) ||
            (x.InventoryAccountId != null && ids.Contains(x.InventoryAccountId.Value)) || (x.CostAccountId != null && ids.Contains(x.CostAccountId.Value)), ct)).Count > 0;

    public Task<CommandReceipt> EditAccount(Guid cid, Guid id, Actor actor, string key, EditAccountRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "account.edit:" + id, request,
            token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var account = await Required<Account>(cid, id, token); Check.Version(account, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب التعديل", 500);
            var candidate = new Account(cid, request.Code, request.Name, request.Type, request.ParentId,
                request.IsPosting, request.IsControl, request.RequiresCostCenter, request.CurrencyCode,
                request.NormalBalance, request.EnglishName, request.Notes);
            var structural = account.Code != candidate.Code || account.Type != candidate.Type || account.ParentId != candidate.ParentId ||
                account.IsPosting != candidate.IsPosting || account.IsControl != candidate.IsControl ||
                account.RequiresCostCenter != candidate.RequiresCostCenter || account.CurrencyCode != candidate.CurrencyCode ||
                account.NormalBalance != candidate.NormalBalance;
            if (structural)
            {
                var accounts = await store.Find<Account>(cid, x => true, token);
                Check.That(!accounts.Any(x => x.Id != id && x.Code == candidate.Code), "duplicate_account", "رقم الحساب مكرر.", ErrorKind.Conflict);
                var byId = accounts.ToDictionary(x => x.Id); var seen = new HashSet<Guid> { id };
                var parentId = candidate.ParentId;
                while (parentId is Guid parent)
                {
                    Check.That(seen.Add(parent), "account_tree_cycle", "لا يمكن ربط الحساب بنفسه أو بأحد فروعه.");
                    Check.That(byId.ContainsKey(parent), "reference_not_found", "الحساب الأب غير موجود ضمن الشركة.", ErrorKind.NotFound);
                    var ancestor = byId[parent];
                    Check.That(ancestor.IsActive && !ancestor.IsPosting && ancestor.Type == candidate.Type,
                        "invalid_parent_account", "الحساب الأب يجب أن يكون رئيسيًا فعالًا ومن التصنيف نفسه.");
                    parentId = ancestor.ParentId;
                }
                var children = accounts.Where(x => x.ParentId == id).ToArray();
                Check.That(!candidate.IsPosting || children.Length == 0, "account_has_children", "الحساب الذي له فروع يبقى رئيسيًا.", ErrorKind.Conflict);
                Check.That(children.All(x => x.Type == candidate.Type), "child_account_type_mismatch", "تصنيف الحساب لا يطابق حساباته الفرعية.", ErrorKind.Conflict);
                if (candidate.CurrencyCode is not null) await currencyService.RequireCurrency(cid, candidate.CurrencyCode, token);
                // Preserve historical report grouping and the meaning of pending/cancelled documents and templates.
                Check.That(!await HasAccountUsage(cid, AccountSubtree(id, accounts), token),
                    "account_structure_in_use", "الحساب أو أحد فروعه مستخدم في قيود أو قوالب أو مجموعات أصناف؛ يلزم مسار تحويل معتمد لتغيير بنيته.", ErrorKind.Conflict);
            }
            var before = AccountSnapshot(account);
            account.Configure(candidate.Code, candidate.Name, candidate.Type, candidate.ParentId, candidate.IsPosting,
                candidate.IsControl, candidate.RequiresCostCenter, candidate.CurrencyCode, candidate.NormalBalance, candidate.EnglishName, candidate.Notes);
            Audit(cid, actor, "account.edited", account, new { before, after = AccountSnapshot(account), reason });
            return Receipt(account, "Updated");
        }, ct);

    public async Task<Account> GetAccount(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct);
        return await Required<Account>(cid, id, ct);
    }

    public async Task<AccountUsage> GetAccountUsage(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Read, ct);
        await Required<Account>(cid, id, ct);
        var accounts = await store.Find<Account>(cid, x => true, ct);
        var journals = await store.Find<JournalEntry>(cid, x => x.Lines.Any(l => l.AccountId == id), ct);
        var templates = await store.Find<RecurringJournalTemplate>(cid, x => x.Lines.Any(l => l.AccountId == id), ct);
        var references = journals.Select(x => new AccountUsageReference("Journal", x.Id, x.BranchId, x.Number, x.State.ToString()))
            .Concat(templates.Select(x => new AccountUsageReference("RecurringTemplate", x.Id, x.BranchId, x.Name, x.IsActive ? "Active" : "Inactive")))
            .OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.NumberOrName, StringComparer.Ordinal).ThenBy(x => x.ResourceId).ToArray();
        return new(id, accounts.Count(x => x.ParentId == id), journals.Count, journals.Sum(x => x.Lines.Count(l => l.AccountId == id)),
            templates.Count, await HasAccountUsage(cid, AccountSubtree(id, accounts), ct),
            new(references.Skip(offset).Take(limit).ToArray(), references.Length, offset, limit));
    }

    public async Task<Page<AuditEvent>> ListAccountAudit(Guid cid, Guid id, Actor actor, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await AuthorizeGlobal(cid, actor, Permissions.Audit, ct);
        await Authorize(cid, actor, Permissions.Read, null, ct); await Required<Account>(cid, id, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
                (x.Action == "account.created" || x.Action == "account.edited" || x.Action == "account.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id), offset, limit, ct);
    }
    public Task<CommandReceipt> CreateAccount(Guid cid, Actor actor, string key, CreateAccountRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "account.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var account = new Account(cid, request.Code, request.Name, request.Type, request.ParentId,
                request.IsPosting, request.IsControl, request.RequiresCostCenter, request.CurrencyCode,
                request.NormalBalance, request.EnglishName, request.Notes);
            Check.That(!(await store.Find<Account>(cid, x => x.Code == account.Code, token)).Any(), "duplicate_account", "رقم الحساب مكرر.", ErrorKind.Conflict);
            if (account.ParentId is Guid parentId)
            {
                var parent = await Required<Account>(cid, parentId, token);
                Check.That(parent.IsActive && !parent.IsPosting && parent.Type == account.Type,
                    "invalid_parent_account", "الحساب الأب يجب أن يكون رئيسيًا فعالًا ومن التصنيف نفسه.");
            }
            if (account.CurrencyCode is not null) await currencyService.RequireCurrency(cid, account.CurrencyCode, token);
            store.Add(account); Audit(cid, actor, "account.created", account, new { after = AccountSnapshot(account) });
            return Receipt(account, "Created");
        }, ct);

    public Task<CommandReceipt> SetAccountStatus(Guid cid, Guid id, Actor actor, string key, SetAccountStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "account.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var account = await Required<Account>(cid, id, token); Check.Version(account, request.ExpectedVersion);
            if (!request.IsActive)
                Check.That(!(await store.Find<Warehouse>(cid, x => x.InventoryAccountId == id && x.IsActive, token)).Any(), "account_used_by_active_warehouse", "الحساب مرتبط بمخزن فعال.", ErrorKind.Conflict);
            if (!request.IsActive)
                Check.That(!(await store.Find<ItemGroup>(cid, x => x.IsActive && (x.SalesAccountId == id || x.InventoryAccountId == id || x.CostAccountId == id), token)).Any(),
                    "account_used_by_active_group", "الحساب مرتبط بمجموعة أصناف فعالة؛ عدل الربط أو عطل المجموعة أولًا.", ErrorKind.Conflict);
            var reason = Check.Text(request.Reason, "السبب", 500);
            if (!request.IsActive)
                Check.That(!(await store.Find<Account>(cid, x => x.ParentId == id && x.IsActive, token)).Any(),
                    "active_child_accounts", "عطّل الحسابات الفرعية أولًا.", ErrorKind.Conflict);
            else if (account.ParentId is Guid parent)
                Check.That((await Required<Account>(cid, parent, token)).IsActive, "inactive_parent", "الحساب الأب غير فعال.");
            var before = AccountSnapshot(account); account.SetActive(request.IsActive);
            Audit(cid, actor, "account.status-changed", account, new { before, after = AccountSnapshot(account), reason });
            return Receipt(account, request.IsActive ? "Active" : "Inactive");
        }, ct);

    public async Task<Page<Account>> ListAccounts(Guid cid, Actor actor, string? search, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit); await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long", "عبارة البحث أطول من المسموح.");
        search = search?.Trim();
        return await store.ReadPage<Account>(cid, x => search == null || x.Code.Contains(search) || x.Name.Contains(search) || (x.EnglishName != null && x.EnglishName.Contains(search)),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }
}
