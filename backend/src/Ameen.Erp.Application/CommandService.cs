using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class ErpService
{
    private readonly IErpStore store;
    private readonly ITransactionRunner transactions;
    private readonly IClock clock;
    public ErpService(IErpStore store, ITransactionRunner transactions, IClock clock)
    { this.store = store; this.transactions = transactions; this.clock = clock; }

    private async Task<Company> Company(Guid id, CancellationToken ct) =>
        await store.GetCompany(id, ct) ?? throw new BusinessException("company_not_found", "الشركة غير موجودة.", ErrorKind.NotFound);

    private async Task<T> Required<T>(Guid companyId, Guid id, CancellationToken ct) where T : CompanyEntity =>
        await store.Get<T>(companyId, id, ct) ?? throw new BusinessException("reference_not_found", "السجل غير موجود ضمن الشركة المحددة.", ErrorKind.NotFound);

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

    private Task<CommandReceipt> Command(Guid companyId, Actor actor, string key, string operation, object body,
        Func<CancellationToken, Task> authorize, Func<CancellationToken, Task<CommandReceipt>> work, CancellationToken ct)
    {
        Check.That(!string.IsNullOrWhiteSpace(actor.Subject), "actor_required", "هوية المستخدم مطلوبة.", ErrorKind.Forbidden);
        Check.Text(actor.Subject, "هوية المستخدم", 200);
        key = Check.Text(key, "Idempotency-Key", 100);
        string data;
        try { data = JsonSerializer.Serialize(body, JsonDefaults.Options); }
        catch (JsonException) { throw new BusinessException("invalid_command_payload", "الطلب يحتوي قيمًا غير قابلة للتمثيل ضمن العقد."); }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        return transactions.Run(new CommandIdentity(companyId, key, operation, actor.Subject, fingerprint), authorize, work, ct);
    }

    private CommandReceipt Receipt(Entity entity, string status, string? number = null) => new(entity.Id, entity.Version, status, number);
    private void Audit(Guid companyId, Actor actor, string action, Entity entity, object details)
        => store.Add(new AuditEvent(companyId, actor.Subject, action, entity.Id, clock.UtcNow, JsonSerializer.Serialize(details, JsonDefaults.Options)));

    public Task<CommandReceipt> CreateCompany(Actor actor, string key, CreateCompanyRequest request, CancellationToken ct) =>
        Command(request.Id, actor, key, "company.create", request, _ =>
        {
            Check.That(actor.IsPlatformAdministrator, "platform_admin_required", "إنشاء الشركات يتطلب مسؤول المنصة.", ErrorKind.Forbidden);
            return Task.CompletedTask;
        }, async token =>
        {
            var company = new Company(request.Id, request.Code, request.Name, request.CountryCode, request.FunctionalCurrency, request.CurrencyScale);
            Check.That(await store.GetCompany(company.Id, token) is null && !await store.CompanyCodeExists(company.Code, token),
                "company_exists", "معرف الشركة أو رمزها مستخدم بالفعل.", ErrorKind.Conflict);
            store.Add(company);
            store.Add(new Currency(company.Id, company.FunctionalCurrency, company.CurrencyScale));
            // Bootstrap does not grant financial approval or posting implicitly.
            store.Add(new Membership(company.Id, actor.Subject,
                new[] { Permissions.Read, Permissions.Configure, Permissions.Members, Permissions.Audit }, true, Array.Empty<Guid>()));
            Audit(company.Id, actor, "company.created", company, new { company.Code, company.FunctionalCurrency });
            return Receipt(company, "Created");
        }, ct);

    public Task<CommandReceipt> CreateBranch(Guid cid, Actor actor, string key, CreateBranchRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "branch.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var branch = new Branch(cid, request.Code, request.Name);
            Check.That(!(await store.Find<Branch>(cid, x => x.Code == branch.Code, token)).Any(), "duplicate_code", "رمز الفرع مكرر.", ErrorKind.Conflict);
            store.Add(branch); Audit(cid, actor, "branch.created", branch, new { branch.Code });
            return Receipt(branch, "Created");
        }, ct);

    public Task<CommandReceipt> CreateCurrency(Guid cid, Actor actor, string key, CreateCurrencyRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "currency.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var currency = new Currency(cid, request.Code, request.Scale);
            Check.That(!(await store.Find<Currency>(cid, x => x.Code == currency.Code, token)).Any(), "duplicate_currency", "العملة معرفة مسبقًا.", ErrorKind.Conflict);
            store.Add(currency); Audit(cid, actor, "currency.created", currency, new { currency.Code, currency.Scale });
            return Receipt(currency, "Created");
        }, ct);

    public Task<CommandReceipt> CreateExchangeRate(Guid cid, Actor actor, string key, CreateExchangeRateRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "exchange-rate.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var company = await Company(cid, token);
            var currency = Check.Currency(request.CurrencyCode);
            Check.That(currency != company.FunctionalCurrency, "functional_rate_fixed", "سعر العملة الوظيفية ثابت ويساوي واحدًا.");
            await RequireCurrency(cid, currency, token);
            Check.That(!(await store.Find<ExchangeRate>(cid, x => x.CurrencyCode == currency && x.EffectiveDate == request.EffectiveDate, token)).Any(),
                "duplicate_exchange_rate", "يوجد سعر لهذه العملة في التاريخ نفسه.", ErrorKind.Conflict);
            var rate = new ExchangeRate(cid, currency, request.EffectiveDate, request.RateToFunctional, request.Source);
            store.Add(rate); Audit(cid, actor, "exchange-rate.created", rate, new { rate.CurrencyCode, rate.EffectiveDate, rate.RateToFunctional });
            return Receipt(rate, "Created");
        }, ct);

    private async Task<Currency> RequireCurrency(Guid cid, string code, CancellationToken ct) =>
        (await store.Find<Currency>(cid, x => x.Code == code, ct)).SingleOrDefault()
        ?? throw new BusinessException("currency_not_found", "العملة غير معرفة في الشركة.");

    public Task<CommandReceipt> CreateFiscalYear(Guid cid, Actor actor, string key, CreateFiscalYearRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "fiscal-year.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var year = new FiscalYear(cid, request.Code, request.StartsOn, request.EndsOn);
            Check.That(!(await store.Find<FiscalYear>(cid, x => x.Code == year.Code || (x.StartsOn <= year.EndsOn && x.EndsOn >= year.StartsOn), token)).Any(),
                "fiscal_year_overlap", "السنة تتداخل مع سنة قائمة أو تكرر رمزها.", ErrorKind.Conflict);
            store.Add(year);
            var current = year.StartsOn; var index = 1;
            while (current <= year.EndsOn)
            {
                var endOfMonth = new DateOnly(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
                var end = endOfMonth < year.EndsOn ? endOfMonth : year.EndsOn;
                store.Add(new FiscalPeriod(cid, year.Id, $"{year.Code}-{index:00}", current, end));
                if (end == year.EndsOn) break;
                current = end.AddDays(1); index++;
            }
            Audit(cid, actor, "fiscal-year.created", year, new { year.StartsOn, year.EndsOn });
            return Receipt(year, "Created");
        }, ct);

    public Task<CommandReceipt> CreateCostCenter(Guid cid, Actor actor, string key, CreateCostCenterRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "cost-center.create", request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            if (request.BranchId is Guid branchId) await ActiveBranch(cid, branchId, token);
            var center = new CostCenter(cid, request.Code, request.Name, request.BranchId);
            Check.That(!(await store.Find<CostCenter>(cid, x => x.Code == center.Code, token)).Any(), "duplicate_code", "رمز المركز مكرر.", ErrorKind.Conflict);
            store.Add(center); Audit(cid, actor, "cost-center.created", center, new { center.Code, center.BranchId });
            return Receipt(center, "Created");
        }, ct);

    public Task<CommandReceipt> GrantMembership(Guid cid, Actor actor, string key, GrantMembershipRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "membership.grant", request, token => AuthorizeGlobal(cid, actor, Permissions.Members, token), async token =>
        {
            Check.That(request.Permissions is not null && request.BranchIds is not null, "invalid_membership", "الصلاحيات والفروع مطلوبة.");
            var subject = Check.Text(request.Subject, "معرف المستخدم");
            Check.That(subject != actor.Subject, "self_grant_forbidden", "لا يمكنك تغيير صلاحياتك بنفسك.", ErrorKind.Forbidden);
            foreach (var branchId in request.BranchIds!) await ActiveBranch(cid, branchId, token);
            var member = (await store.Find<Membership>(cid, x => x.Subject == subject, token)).SingleOrDefault();
            var before = member is null ? null : new { member.GrantedPermissions, member.AllBranches, member.BranchIds, member.Version };
            if (member is null)
            {
                Check.That(request.ExpectedVersion is null, "membership_missing", "العضوية المطلوب تعديلها غير موجودة.", ErrorKind.Conflict);
                member = new Membership(cid, subject, request.Permissions!, request.AllBranches, request.BranchIds!);
                store.Add(member);
            }
            else
            {
                Check.Version(member, request.ExpectedVersion ?? Guid.Empty);
                member.Configure(request.Permissions!, request.AllBranches, request.BranchIds!);
            }
            Audit(cid, actor, "membership.granted", member, new { subject, before, after = new { member.GrantedPermissions, member.AllBranches, member.BranchIds } });
            return Receipt(member, "Granted");
        }, ct);

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
            if (account.CurrencyCode is not null) await RequireCurrency(cid, account.CurrencyCode, token);
            store.Add(account); Audit(cid, actor, "account.created", account, new { after = AccountSnapshot(account) });
            return Receipt(account, "Created");
        }, ct);

    public Task<CommandReceipt> SetMembershipStatus(Guid cid, Guid id, Actor actor, string key, SetMembershipStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "membership.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Members, token), async token =>
        {
            var member = await Required<Membership>(cid, id, token); Check.Version(member, request.ExpectedVersion);
            Check.That(member.Subject != actor.Subject, "self_grant_forbidden", "لا يمكنك تغيير حالة عضويتك بنفسك.", ErrorKind.Forbidden);
            var reason = Check.Text(request.Reason, "السبب", 500); var before = member.IsActive;
            member.SetActive(request.IsActive);
            Audit(cid, actor, "membership.status-changed", member, new { before, after = member.IsActive, reason });
            return Receipt(member, member.IsActive ? "Active" : "Inactive");
        }, ct);

    public Task<CommandReceipt> SetAccountStatus(Guid cid, Guid id, Actor actor, string key, SetAccountStatusRequest request, CancellationToken ct) =>
        Command(cid, actor, key, "account.status:" + id, request, token => AuthorizeGlobal(cid, actor, Permissions.Configure, token), async token =>
        {
            var account = await Required<Account>(cid, id, token); Check.Version(account, request.ExpectedVersion);
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

    private async Task<Branch> ActiveBranch(Guid cid, Guid id, CancellationToken ct)
    {
        var branch = await Required<Branch>(cid, id, ct);
        Check.That(branch.IsActive, "inactive_branch", "الفرع غير فعال."); return branch;
    }

    public Task<CommandReceipt> ChangePeriod(Guid cid, Guid id, Actor actor, string key, ChangePeriodRequest request, bool close, CancellationToken ct) =>
        Command(cid, actor, key, $"period.{(close ? "close" : "reopen")}:{id}", request,
            token => AuthorizeGlobal(cid, actor, close ? Permissions.PeriodClose : Permissions.PeriodReopen, token), async token =>
        {
            var period = await Required<FiscalPeriod>(cid, id, token); Check.Version(period, request.ExpectedVersion);
            var reason = Check.Text(request.Reason, "سبب الإقفال أو الفتح", 500);
            if (close)
            {
                var pending = await store.Find<JournalEntry>(cid, x => x.FiscalPeriodId == id &&
                    (x.State == JournalState.Draft || x.State == JournalState.Submitted || x.State == JournalState.Approved), token);
                Check.That(pending.Count == 0, "unposted_journals", "الفترة تحتوي قيودًا غير مرحلة؛ عالجها قبل الإقفال.", ErrorKind.Conflict);
                var posted = await store.Find<JournalEntry>(cid, x => x.FiscalPeriodId == id &&
                    (x.State == JournalState.Posted || x.State == JournalState.Reversed), token);
                foreach (var journal in posted) journal.ValidateBalance();
            }
            period.SetClosed(close);
            Audit(cid, actor, close ? "period.closed" : "period.reopened", period, new { reason, GeneralLedgerOnly = true });
            return Receipt(period, close ? "Closed" : "Open");
        }, ct);
}
