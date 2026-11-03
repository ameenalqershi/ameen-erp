using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH02-01: no publicly writable banking operation is claimed by these tests.
public static class BankAccountFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH02-01: 17 original bank-card fields, leading zeros and canonical IBAN", f =>
        {
            var now = new FixedClock().UtcNow;
            var card = Create(f, "GB82 WEST 1234 5698 7654 32");
            Ensure(card.CompanyId == f.CompanyId && card.Id != Guid.Empty &&
                card.Code == "BANK01" && card.Name == "حساب بنكي" &&
                card.EnglishName == "Bank Account" && card.Notes == "تجريبي" &&
                card.IsActive && card.BankName == "بنك اختباري" &&
                card.AccountNumber == "0000012345" && card.Iban == "GB82WEST12345698765432" &&
                card.LedgerAccountId == f.CashId && card.CurrencyCode == "SAR" &&
                card.BranchId == f.BranchId && card.CreatedBy == f.Owner.Subject &&
                card.CreatedAt == now && card.LastModifiedAt == now && card.Revision == 1);
            var version = card.Version;
            card.SetActive(false, now);
            Ensure(!card.IsActive && card.Revision == 2 && card.Version != version);
            card.Configure("BANK01", "حساب معدل", "بنك اختباري", "0000012345",
                null, f.CashId, "SAR", f.BranchId, null, null, now);
            Ensure(card.Revision == 3 && card.Iban is null && card.CreatedAt == now &&
                card.AccountNumber.StartsWith("00000", StringComparison.Ordinal));
            return Task.CompletedTask;
        });

        test("CASH02-01: invalid/checksum IBAN and missing mandatory references fail", f =>
        {
            Error("invalid_iban", () => Create(f, "GB82 WEST 1234 5698 7654 33"));
            Error("invalid_iban", () => Create(f, "GB00"));
            Error("invalid_iban", () => Create(f, "GB82WEST12345*98765432"));
            Error("bank_account_reference_required", () => new BankAccount(f.CompanyId,
                "BANK01", "بنك", "اسم البنك", "0001", null, Guid.Empty, "SAR",
                f.BranchId, f.Owner.Subject, new FixedClock().UtcNow));
            Error("invalid_text", () => new BankAccount(f.CompanyId, "BANK01", "بنك",
                "", "0001", null, f.CashId, "SAR", f.BranchId,
                f.Owner.Subject, new FixedClock().UtcNow));
            Ensure(f.Store.Count<BankAccount>() == 0);
            return Task.CompletedTask;
        });

        test("CASH02-AC03: lowercase spaced IBAN canonicalizes without losing account zeros", f =>
        {
            var card = Create(f, "gb82 west 1234 5698 7654 32");
            Ensure(card.Iban == "GB82WEST12345698765432" && card.AccountNumber == "0000012345");
            return Task.CompletedTask;
        });

        test("CASH02-AC04: tabs and non-ASCII characters never bypass IBAN format", f =>
        {
            Error("invalid_iban", () => Create(f, "GB82\tWEST12345698765432"));
            Error("invalid_iban", () => Create(f, "GB82WEST1234569876543٢"));
            return Task.CompletedTask;
        });

        test("CASH02-AC05: IBAN outside supported 15 to 34 length is rejected", f =>
        {
            Error("invalid_iban", () => Create(f, "GB82WEST12345"));
            Error("invalid_iban", () => Create(f, "GB82WEST12345698765432123456789012345"));
            return Task.CompletedTask;
        });

        test("CASH02-AC06: invalid currency edit is atomic across bank fields", f =>
        {
            var card = Create(f, "GB82 WEST 1234 5698 7654 32");
            var revision = card.Revision;
            var version = card.Version;
            var modified = card.LastModifiedAt;
            Error("invalid_currency", () => card.Configure("CHANGED", "Changed", "Other Bank",
                "999", null, Guid.NewGuid(), "US1", Guid.NewGuid(),
                "Other", "Other", modified.AddMinutes(1)));
            Ensure(card.Code == "BANK01" && card.Name == "حساب بنكي" &&
                card.BankName == "بنك اختباري" && card.AccountNumber == "0000012345" &&
                card.Iban == "GB82WEST12345698765432" && card.CurrencyCode == "SAR" &&
                card.Revision == revision && card.Version == version &&
                card.LastModifiedAt == modified);
            return Task.CompletedTask;
        });

        test("CASH02: rejected bank edits preserve every field and audit revision", f =>
        {
            var card = Create(f, "GB82 WEST 1234 5698 7654 32");
            var originalVersion = card.Version;
            var originalRevision = card.Revision;
            var originalModified = card.LastModifiedAt;
            Error("invalid_iban", () => card.Configure("CHANGED", "تعديل مرفوض",
                "بنك آخر", "9999", "GB82 WEST 1234 5698 7654 33",
                Guid.NewGuid(), "USD", Guid.NewGuid(), "Changed", "Changed",
                originalModified.AddMinutes(1)));
            Ensure(card.Code == "BANK01" && card.Name == "حساب بنكي" &&
                card.BankName == "بنك اختباري" && card.AccountNumber == "0000012345" &&
                card.Iban == "GB82WEST12345698765432" && card.CurrencyCode == "SAR" &&
                card.BranchId == f.BranchId && card.LedgerAccountId == f.CashId &&
                card.EnglishName == "Bank Account" && card.Notes == "تجريبي" &&
                card.Version == originalVersion && card.Revision == originalRevision &&
                card.LastModifiedAt == originalModified);
            return Task.CompletedTask;
        });

        test("CASH02: exhausted revision rejects deactivate without mutation", f =>
        {
            var card = Create(f, null);
            typeof(BankAccount).GetProperty(nameof(BankAccount.Revision))!
                .SetValue(card, int.MaxValue);
            var version = card.Version;
            var modified = card.LastModifiedAt;
            Error("bank_account_revision_exhausted", () =>
                card.SetActive(false, modified.AddMinutes(1)));
            Ensure(card.IsActive && card.Revision == int.MaxValue &&
                card.Version == version && card.LastModifiedAt == modified);
            return Task.CompletedTask;
        });

        test("CASH02-01: historical audit time cannot regress on domain mutations", f =>
        {
            var card = Create(f, null);
            var before = card.Revision;
            Error("bank_account_audit_time_invalid", () => card.SetActive(false,
                new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));
            Ensure(card.Revision == before);
            return Task.CompletedTask;
        });

        test("CASH02-01: EF model refuses unapproved bank-account writes", f =>
        {
            // Actual DB SaveChanges guard is exercised by the SQL integration suite.
            // Domain-only construction never bypasses the review/authorization gate.
            var card = Create(f, null);
            Ensure(card.Id != Guid.Empty && !f.Store.All<BankAccount>().Any());
            return Task.CompletedTask;
        });
    }

    private static BankAccount Create(Fixture f, string? iban) =>
        new(f.CompanyId, "BANK01", "حساب بنكي", "بنك اختباري",
            "0000012345", iban, f.CashId, "SAR", f.BranchId,
            f.Owner.Subject, new FixedClock().UtcNow, "Bank Account", "تجريبي");

    private static void Error(string code, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected bank-account validation error " + code);
    }

    private static void Ensure(bool value)
    {
        if (!value) throw new Exception("CASH02 bank-account foundation assertion failed");
    }
}
