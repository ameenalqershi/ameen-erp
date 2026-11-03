using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed class CashBoxService : ApplicationService
{
    public CashBoxService(IErpStore store, ITransactionRunner transactions, IClock clock)
        : base(store, transactions, clock) { }

    private static object Snapshot(CashBox x) => new
    {
        x.Code, x.Name, x.EnglishName, x.Notes, x.IsActive, x.AccountId,
        x.BranchId, x.CustodianMembershipId, x.CurrencyCode, x.BalanceLimit,
        x.CreatedBy, x.CreatedAt, x.Revision, x.LastModifiedAt, x.Version
    };

    private async Task ValidateReferences(Guid cid, Guid accountId, Guid branchId,
        Guid custodianId, string currencyCode, decimal? balanceLimit, CancellationToken ct)
    {
        var branch = await Required<Branch>(cid, branchId, ct);
        Check.That(branch.IsActive && branch.IsProfileComplete, "invalid_cashbox_branch",
            "الفرع المعطل أو غير المستكمل لا يقبل صندوقًا.");
        var account = await Required<Account>(cid, accountId, ct);
        Check.That(account.IsActive && account.IsPosting && !account.IsControl,
            "invalid_cashbox_account", "حساب الصندوق يجب أن يكون تفصيليًا فعالًا وغير رقابي.");
        Check.That(account.CurrencyCode is null || account.CurrencyCode == currencyCode,
            "cashbox_currency_mismatch", "عملة الصندوق لا توافق عملة حسابه.");
        var currency = (await store.Find<Currency>(cid, x => x.Code == currencyCode, ct)).SingleOrDefault();
        Check.That(currency is not null && currency.IsActive, "invalid_cashbox_currency",
            "عملة الصندوق غير موجودة أو معطلة.");
        if (balanceLimit is decimal limit) Check.Amount(limit, currency!.Scale);
        var custodian = await Required<Membership>(cid, custodianId, ct);
        Check.That(custodian.IsActive && custodian.Allows(branchId),
            "invalid_cashbox_custodian", "أمين الصندوق غير فعال أو لا يملك صلاحية الفرع.");
    }

    private async Task EnsureUnique(Guid cid, Guid? ownId, CashBox box, CancellationToken ct)
    {
        var existing = await store.Find<CashBox>(cid, x => x.Code == box.Code ||
            (x.AccountId == box.AccountId && x.BranchId == box.BranchId &&
                x.CurrencyCode == box.CurrencyCode), ct);
        Check.That(!existing.Any(x => x.Id != ownId && x.Code == box.Code),
            "duplicate_code", "رمز الصندوق مستخدم.", ErrorKind.Conflict);
        Check.That(!existing.Any(x => x.Id != ownId && x.AccountId == box.AccountId &&
            x.BranchId == box.BranchId && x.CurrencyCode == box.CurrencyCode),
            "cashbox_account_already_linked",
            "هذا الحساب مرتبط بالفعل بصندوق في الفرع والعملة نفسيهما.", ErrorKind.Conflict);
    }

    public Task<CommandReceipt> CreateCashBox(Guid cid, Actor actor, string key,
        CreateCashBoxRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "cashbox.create", request,
            token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
            {
                var box = new CashBox(cid, request.Code, request.Name, request.AccountId,
                    request.BranchId, request.CustodianMembershipId, request.CurrencyCode,
                    request.BalanceLimit, actor.Subject, clock.UtcNow,
                    request.EnglishName, request.Notes);
                await ValidateReferences(cid, box.AccountId, box.BranchId,
                    box.CustodianMembershipId, box.CurrencyCode, box.BalanceLimit, token);
                await EnsureUnique(cid, null, box, token);
                store.Add(box);
                Audit(cid, actor, "cashbox.created", box, new { after = Snapshot(box) });
                return Receipt(box, "Created");
            }, ct);

    public Task<CommandReceipt> EditCashBox(Guid cid, Guid id, Actor actor, string key,
        EditCashBoxRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "cashbox.edit:" + id, request,
            token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
            {
                var existing = await Required<CashBox>(cid, id, token);
                Check.Version(existing, request.ExpectedVersion);
                var reason = Check.Text(request.Reason, "سبب تعديل الصندوق", 500);
                var candidate = new CashBox(cid, request.Code, request.Name, request.AccountId,
                    request.BranchId, request.CustodianMembershipId, request.CurrencyCode,
                    request.BalanceLimit, actor.Subject, clock.UtcNow,
                    request.EnglishName, request.Notes);
                await ValidateReferences(cid, candidate.AccountId, candidate.BranchId,
                    candidate.CustodianMembershipId, candidate.CurrencyCode, candidate.BalanceLimit, token);
                await EnsureUnique(cid, id, candidate, token);
                var before = Snapshot(existing);
                existing.Configure(candidate.Code, candidate.Name, candidate.AccountId,
                    candidate.BranchId, candidate.CustodianMembershipId, candidate.CurrencyCode,
                    candidate.BalanceLimit, candidate.EnglishName, candidate.Notes, clock.UtcNow);
                Audit(cid, actor, "cashbox.edited", existing,
                    new { before, after = Snapshot(existing), reason });
                return Receipt(existing, "Updated");
            }, ct);

    public Task<CommandReceipt> SetCashBoxStatus(Guid cid, Guid id, Actor actor, string key,
        SetCashBoxStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "cashbox.status:" + id, request,
            token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
            {
                var box = await Required<CashBox>(cid, id, token);
                Check.Version(box, request.ExpectedVersion);
                var reason = Check.Text(request.Reason, "سبب تغيير حالة الصندوق", 500);
                if (request.IsActive)
                    await ValidateReferences(cid, box.AccountId, box.BranchId,
                        box.CustodianMembershipId, box.CurrencyCode, box.BalanceLimit, token);
                var before = Snapshot(box);
                box.SetActive(request.IsActive, clock.UtcNow);
                Audit(cid, actor, "cashbox.status-changed", box,
                    new { before, after = Snapshot(box), reason });
                return Receipt(box, box.IsActive ? "Active" : "Inactive");
            }, ct);

    public async Task<CashBox> GetCashBox(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await Authorize(cid, actor, Permissions.Read, null, ct);
        var box = await Required<CashBox>(cid, id, ct);
        await Authorize(cid, actor, Permissions.Read, box.BranchId, ct);
        return box;
    }

    public async Task<Page<CashBox>> SearchCashBoxes(Guid cid, Actor actor, string? search,
        Guid? branchId, bool? isActive, int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        var member = await Authorize(cid, actor, Permissions.Read, null, ct);
        Check.That(search is null || search.Length <= 100, "search_too_long",
            "عبارة البحث أطول من المسموح.");
        var branches = await AllowedBranches(cid, member, branchId, ct);
        if (branchId is Guid explicitBranch)
            Check.That(member.Allows(explicitBranch), "branch_access_denied",
                "الفرع خارج نطاق صلاحياتك.", ErrorKind.Forbidden);
        search = search?.Trim();
        return await store.ReadPage<CashBox>(cid, x =>
            branches.Contains(x.BranchId) &&
            (isActive == null || x.IsActive == isActive) &&
            (search == null || x.Code.Contains(search) || x.Name.Contains(search) ||
                (x.EnglishName != null && x.EnglishName.Contains(search))),
            q => q.OrderBy(x => x.Code).ThenBy(x => x.Id), offset, limit, ct);
    }

    public async Task<CashBoxUsage> GetCashBoxUsage(Guid cid, Guid id, Actor actor,
        CancellationToken ct)
    {
        var box = await GetCashBox(cid, id, actor, ct);
        var journals = await store.Find<JournalEntry>(cid, x => x.BranchId == box.BranchId &&
            x.Lines.Any(line => line.AccountId == box.AccountId &&
                line.CurrencyCode == box.CurrencyCode), ct);
        return new(box.Id, journals.Count, journals.Count > 0);
    }

    public async Task<Page<AuditEvent>> ListCashBoxAudit(Guid cid, Guid id, Actor actor,
        int offset, int limit, CancellationToken ct)
    {
        Paging(offset, limit);
        var box = await GetCashBox(cid, id, actor, ct);
        await Authorize(cid, actor, Permissions.Audit, box.BranchId, ct);
        return await store.ReadPage<AuditEvent>(cid, x => x.ResourceId == id &&
            (x.Action == "cashbox.created" || x.Action == "cashbox.edited" ||
                x.Action == "cashbox.status-changed"),
            q => q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id),
            offset, limit, ct);
    }
}
