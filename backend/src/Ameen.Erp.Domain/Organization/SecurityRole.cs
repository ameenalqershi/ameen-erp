namespace Ameen.Erp.Domain;

// SYS07 structural foundation. These definitions are not connected to authentication,
// membership grants or SYS09 approvals until explicit-deny and scope policy is accepted.
public sealed class SecurityRole : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string Name { get; private set; } = "";
    public string Responsibility { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<SecurityRoleRule> Rules { get; private set; } = new();

    private SecurityRole() { }

    public SecurityRole(Guid companyId, Guid? branchId, string name, string responsibility,
        string actor, DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "invalid_role_branch", "الفرع غير صالح.");
        BranchId = branchId;
        Name = Check.Text(name, "الدور", 200);
        Responsibility = Check.Text(responsibility, "وصف المسؤولية", 500);
        CreatedBy = Check.Text(actor, "منشئ الدور", 200);
        CreatedAt = LastModifiedAt = now;
    }

    public void Edit(string name, string responsibility, DateTimeOffset now)
    {
        var nextName = Check.Text(name, "الدور", 200);
        var nextResponsibility = Check.Text(responsibility, "وصف المسؤولية", 500);
        Advance(now);
        Name = nextName;
        Responsibility = nextResponsibility;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        Advance(now);
        IsActive = active;
    }

    public SecurityRoleRule AddRule(string screen, string operation, bool allowed,
        decimal? amountLimit, Guid? currencyId, DateTimeOffset now)
    {
        var rule = new SecurityRoleRule(CompanyId, Id, screen, operation, allowed, amountLimit, currencyId);
        Check.That(!Rules.Any(x => x.ScreenCode == rule.ScreenCode &&
            x.OperationCode == rule.OperationCode && x.CurrencyId == rule.CurrencyId),
            "duplicate_role_rule", "لا يمكن تكرار العملية والعملة داخل الدور.");
        Advance(now);
        Rules.Add(rule);
        return rule;
    }

    // A pure conservative evaluator, NOT yet wired into user authorization.
    // Any explicit deny wins; an amount-limited grant requires a matching currency
    // and a supplied amount. No implicit permission is ever inferred.
    public bool Permits(string screen, string operation, decimal? amount, Guid? currencyId)
    {
        if (!IsActive || amount < 0 || string.IsNullOrWhiteSpace(screen) || string.IsNullOrWhiteSpace(operation))
            return false;
        var matches = Rules.Where(x =>
            x.ScreenCode == screen.ToUpperInvariant() &&
            x.OperationCode == operation.ToUpperInvariant()).ToArray();
        if (matches.Any(x => !x.Allowed)) return false;
        return matches.Any(x => x.Allowed && (x.AmountLimit is null ||
            amount is >= 0 && currencyId == x.CurrencyId && amount <= x.AmountLimit));
    }

    private void Advance(DateTimeOffset now)
    {
        Check.That(Revision < int.MaxValue && now >= CreatedAt && now >= LastModifiedAt,
            "invalid_role_revision", "وقت أو إصدار الدور غير صالح.", ErrorKind.Conflict);
        Revision++;
        LastModifiedAt = now;
        Touch();
    }
}

public sealed class SecurityRoleRule : CompanyEntity
{
    public Guid RoleId { get; private set; }
    public string ScreenCode { get; private set; } = "";
    public string OperationCode { get; private set; } = "";
    public bool Allowed { get; private set; }
    public decimal? AmountLimit { get; private set; }
    public Guid? CurrencyId { get; private set; }

    private SecurityRoleRule() { }

    internal SecurityRoleRule(Guid companyId, Guid roleId, string screen, string operation,
        bool allowed, decimal? amountLimit, Guid? currencyId) : base(companyId)
    {
        Check.That(roleId != Guid.Empty, "invalid_role_reference", "مرجع الدور غير صالح.");
        ScreenCode = Check.Code(screen, "الشاشة", 32);
        OperationCode = Check.Code(operation, "العملية", 64);
        Check.That((amountLimit is null && currencyId is null) ||
            (allowed && amountLimit is not null && currencyId is not null && currencyId != Guid.Empty &&
             amountLimit >= 0 && amountLimit < 100_000_000_000_000m &&
             decimal.Round(amountLimit.Value, 6) == amountLimit),
            "invalid_role_amount_policy", "حد المبلغ وعملته مطلوبان معًا لمنح مقيد، ولا يقبل المنع سقفًا.");
        RoleId = roleId;
        Allowed = allowed;
        AmountLimit = amountLimit;
        CurrencyId = currencyId;
    }
}
