namespace Ameen.Erp.Domain;

// CASH01 is a master card, not a balance ledger. Available cash is enforced
// by the future CASH03/CASH04 posting transaction, never by this card.
public sealed class CashBox : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustodianMembershipId { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public decimal? BalanceLimit { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CashBox() { }

    public CashBox(Guid companyId, string code, string name, Guid accountId, Guid branchId,
        Guid custodianMembershipId, string currencyCode, decimal? balanceLimit,
        string createdBy, DateTimeOffset createdAt, string? englishName = null,
        string? notes = null) : base(companyId)
    {
        Check.That(createdAt != default, "cashbox_created_at_required",
            "وقت إنشاء بطاقة الصندوق مطلوب.");
        CreatedBy = Check.Text(createdBy, "منشئ بطاقة الصندوق");
        CreatedAt = createdAt;
        Configure(code, name, accountId, branchId, custodianMembershipId,
            currencyCode, balanceLimit, englishName, notes, createdAt);
    }

    public void Configure(string code, string name, Guid accountId, Guid branchId,
        Guid custodianMembershipId, string currencyCode, decimal? balanceLimit,
        string? englishName, string? notes, DateTimeOffset now)
    {
        Check.That(now >= CreatedAt && now >= LastModifiedAt,
            "cashbox_audit_time_invalid", "وقت التعديل لا يسبق السجل السابق.");
        Check.That(accountId != Guid.Empty && branchId != Guid.Empty &&
            custodianMembershipId != Guid.Empty, "cashbox_reference_required",
            "حساب الصندوق والفرع والأمين مطلوبة.");
        // Validate all fields before mutating an EF-tracked cash box.
        var checkedCode = Check.Code(code, "رمز الصندوق");
        var checkedName = Check.Text(name, "اسم الصندوق");
        var checkedEnglishName = englishName is null ? null : Check.Text(englishName, "اسم الصندوق الإنجليزي");
        var checkedNotes = notes is null ? null : Check.Text(notes, "ملاحظات الصندوق", 2000);
        var checkedCurrency = Check.Currency(currencyCode);
        if (balanceLimit is decimal limit) Check.Amount(limit);
        Check.That(Revision < int.MaxValue, "cashbox_revision_exhausted",
            "بلغ إصدار بطاقة الصندوق الحد الأقصى.", ErrorKind.Conflict);
        Code = checkedCode;
        Name = checkedName;
        EnglishName = checkedEnglishName;
        Notes = checkedNotes;
        AccountId = accountId;
        BranchId = branchId;
        CustodianMembershipId = custodianMembershipId;
        CurrencyCode = checkedCurrency;
        BalanceLimit = balanceLimit;
        Advance(now);
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        Check.That(now >= CreatedAt && now >= LastModifiedAt,
            "cashbox_audit_time_invalid", "وقت التعديل لا يسبق السجل السابق.");
        Check.That(Revision < int.MaxValue, "cashbox_revision_exhausted",
            "بلغ إصدار بطاقة الصندوق الحد الأقصى.", ErrorKind.Conflict);
        IsActive = active;
        Advance(now);
    }

    private void Advance(DateTimeOffset now)
    {
        Check.That(Revision < int.MaxValue, "cashbox_revision_exhausted",
            "بلغ إصدار بطاقة الصندوق الحد الأقصى.", ErrorKind.Conflict);
        Check.That(now >= CreatedAt && now >= LastModifiedAt,
            "cashbox_audit_time_invalid", "وقت التعديل لا يسبق السجل السابق.");
        Revision++;
        LastModifiedAt = now;
        Touch();
    }
}
