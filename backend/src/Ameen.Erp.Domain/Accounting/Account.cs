namespace Ameen.Erp.Domain;

public sealed class Account : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public NormalBalance NormalBalance { get; private set; }
    public int Revision { get; private set; }
    public AccountType Type { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsPosting { get; private set; }
    public bool IsControl { get; private set; }
    public bool RequiresCostCenter { get; private set; }
    public string? CurrencyCode { get; private set; }
    public bool IsActive { get; private set; } = true;
    private Account() { }
    public Account(Guid companyId, string code, string name, AccountType type, Guid? parentId,
        bool posting, bool control, bool requiresCenter, string? currency, NormalBalance normalBalance,
        string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, type, parentId, posting, control, requiresCenter, currency, normalBalance, englishName, notes);

    // Cross-record tree/usage checks belong to the serialized application command.
    public void Configure(string code, string name, AccountType type, Guid? parentId,
        bool posting, bool control, bool requiresCenter, string? currency, NormalBalance normalBalance,
        string? englishName, string? notes)
    {
        Check.That(Enum.IsDefined(typeof(AccountType), type), "invalid_account_type", "تصنيف الحساب غير صالح.");
        Check.That(Enum.IsDefined(normalBalance), "invalid_normal_balance", "طبيعة رصيد الحساب غير صالحة.");
        Code = Check.Code(code, "رقم الحساب"); Name = Check.Text(name, "اسم الحساب");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        NormalBalance = normalBalance;
        Type = type; ParentId = parentId; IsPosting = posting; IsControl = control;
        RequiresCostCenter = requiresCenter;
        CurrencyCode = currency is null ? null : Check.Currency(currency);
        AdvanceRevision();
    }
    public void SetActive(bool active) { IsActive = active; AdvanceRevision(); }
    private void AdvanceRevision()
    {
        Check.That(Revision < int.MaxValue, "account_revision_exhausted", "بلغ إصدار الحساب الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}
