using System.Text.RegularExpressions;

namespace Ameen.Erp.Domain;

// CASH02 foundation only. No bank transaction, beneficiary approval or payment policy is active.
public sealed class BankAccount : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public string BankName { get; private set; } = "";
    public string AccountNumber { get; private set; } = "";
    public string? Iban { get; private set; }
    public Guid LedgerAccountId { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public Guid BranchId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private BankAccount() { }

    public BankAccount(Guid companyId, string code, string name, string bankName,
        string accountNumber, string? iban, Guid ledgerAccountId, string currencyCode,
        Guid branchId, string createdBy, DateTimeOffset createdAt,
        string? englishName = null, string? notes = null) : base(companyId)
    {
        CreatedBy = Check.Text(createdBy, "منشئ الحساب البنكي");
        CreatedAt = createdAt;
        Configure(code, name, bankName, accountNumber, iban, ledgerAccountId, currencyCode,
            branchId, englishName, notes, createdAt);
    }

    public void Configure(string code, string name, string bankName, string accountNumber,
        string? iban, Guid ledgerAccountId, string currencyCode, Guid branchId,
        string? englishName, string? notes, DateTimeOffset now)
    {
        Check.That(now >= CreatedAt && now >= LastModifiedAt,
            "bank_account_audit_time_invalid", "وقت التعديل لا يسبق وقت إنشاء الحساب البنكي.");
        Check.That(ledgerAccountId != Guid.Empty && branchId != Guid.Empty,
            "bank_account_reference_required", "حساب الأستاذ والفرع مطلوبان.");
        // Validate every input before mutating the tracked entity. A rejected IBAN or
        // currency must not leave partially edited bank details in the EF change tracker.
        var checkedCode = Check.Code(code, "رمز الحساب البنكي");
        var checkedName = Check.Text(name, "اسم الحساب البنكي");
        var checkedEnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        var checkedNotes = notes is null ? null : Check.Text(notes, "ملاحظات", 2000);
        var checkedBankName = Check.Text(bankName, "اسم البنك");
        // Preserve account numbers including leading zeros. No numerical conversion.
        var checkedAccountNumber = Check.Text(accountNumber, "رقم الحساب البنكي", 64);
        var checkedIban = NormalizeIban(iban);
        var checkedCurrency = Check.Currency(currencyCode);
        Check.That(Revision < int.MaxValue, "bank_account_revision_exhausted",
            "بلغ إصدار بطاقة الحساب البنكي الحد الأقصى.", ErrorKind.Conflict);
        Code = checkedCode;
        Name = checkedName;
        EnglishName = checkedEnglishName;
        Notes = checkedNotes;
        BankName = checkedBankName;
        AccountNumber = checkedAccountNumber;
        Iban = checkedIban;
        LedgerAccountId = ledgerAccountId;
        CurrencyCode = checkedCurrency;
        BranchId = branchId;
        Advance(now);
    }

    // Validates only supplied IBANs. Whether an IBAN is mandatory for a country/
    // account type is deliberately deferred until the country's policy is approved.
    public static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        var normalized = new string(iban.Where(c => c != ' ').ToArray()).ToUpperInvariant();
        Check.That(normalized.Length is >= 15 and <= 34 &&
            Regex.IsMatch(normalized, @"^[A-Z]{2}[0-9]{2}[A-Z0-9]+$", RegexOptions.CultureInvariant),
            "invalid_iban", "صيغة IBAN غير صالحة.");
        var reordered = normalized[4..] + normalized[..4];
        var remainder = 0;
        foreach (var ch in reordered)
        {
            if (ch is >= '0' and <= '9') remainder = (remainder * 10 + ch - '0') % 97;
            else remainder = (remainder * 100 + ch - 'A' + 10) % 97;
        }
        Check.That(remainder == 1, "invalid_iban", "تحقق IBAN (MOD-97) فشل.");
        return normalized;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        Check.That(now >= CreatedAt && now >= LastModifiedAt,
            "bank_account_audit_time_invalid", "وقت التعديل لا يسبق وقت إنشاء الحساب البنكي.");
        IsActive = active;
        Advance(now);
    }

    private void Advance(DateTimeOffset now)
    {
        Check.That(Revision < int.MaxValue, "bank_account_revision_exhausted",
            "بلغ إصدار بطاقة الحساب البنكي الحد الأقصى.", ErrorKind.Conflict);
        Check.That(now >= CreatedAt && now >= LastModifiedAt, "bank_account_audit_time_invalid",
            "وقت التعديل لا يسبق وقت إنشاء الحساب البنكي.");
        Revision++;
        LastModifiedAt = now;
        Touch();
    }
}
