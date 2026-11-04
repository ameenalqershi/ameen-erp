using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// PUR01 domain-only acceptance. No supplier purchasing, payables or bank edits are enabled.
public static class SupplierFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("PUR01-FG01..FG07: supplier card retains every original field without accounting writes", f =>
        {
            var beforeJournals = f.Store.Count<JournalEntry>();
            var beforeOutbox = f.Store.Count<OutboxEvent>();
            var beforeReceipts = f.Store.Count<IdempotencyRecord>();
            var now = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            var country = Guid.NewGuid();
            var supplier = new Supplier(f.CompanyId, "00009", "مورد", "شركة المورد القانونية",
                f.EquityId, currency, "30 يومًا", 30, country, "صنعاء", "شارع التحرير", true,
                f.Owner.Subject, now, englishName: "Supplier", notes: "ملاحظة",
                taxNumber: "TAX0009", bankBeneficiary: "صاحب الحساب", iban: "YE12TEST",
                district: "التحرير", buildingNumber: "0002", postalCode: "00004",
                additionalNumber: "0007");
            Ensure(supplier.Id != Guid.Empty && supplier.Version != Guid.Empty &&
                supplier.CompanyId == f.CompanyId && supplier.Code == "00009" &&
                supplier.Name == "مورد" && supplier.EnglishName == "Supplier" &&
                supplier.IsActive && supplier.Notes == "ملاحظة" &&
                supplier.LegalName == "شركة المورد القانونية" &&
                supplier.ControlAccountId == f.EquityId &&
                supplier.TransactionCurrencyId == currency &&
                supplier.PaymentTerms == "30 يومًا" && supplier.DueDays == 30 &&
                supplier.TaxNumber == "TAX0009" && supplier.CountryId == country &&
                supplier.BankBeneficiary == "صاحب الحساب" && supplier.Iban == "YE12TEST" &&
                supplier.CreatedBy == f.Owner.Subject && supplier.CreatedAt == now &&
                supplier.Revision == 1 && supplier.LastModifiedAt == now &&
                supplier.City == "صنعاء" && supplier.District == "التحرير" &&
                supplier.Street == "شارع التحرير" && supplier.BuildingNumber == "0002" &&
                supplier.PostalCode == "00004" && supplier.AdditionalNumber == "0007",
                "PUR01 supplier field fidelity failed");
            Ensure(f.Store.Count<JournalEntry>() == beforeJournals &&
                f.Store.Count<OutboxEvent>() == beforeOutbox &&
                f.Store.Count<IdempotencyRecord>() == beforeReceipts,
                "PUR01 supplier construction produced financial, outbox or receipt writes");
            return Task.CompletedTask;
        });
        test("PUR01: optional fields remain optional; zero due days and inactive state are allowed", f =>
        {
            var supplier = new Supplier(f.CompanyId, "SUP-X", "مورد", "شركة المورد",
                f.EquityId, Guid.NewGuid(), "عند الاستلام", 0, Guid.NewGuid(),
                "صنعاء", "شارع", false, f.Owner.Subject, DateTimeOffset.UtcNow);
            Ensure(!supplier.IsActive && supplier.EnglishName is null && supplier.Notes is null &&
                supplier.TaxNumber is null && supplier.BankBeneficiary is null &&
                supplier.Iban is null && supplier.District is null &&
                supplier.BuildingNumber is null && supplier.PostalCode is null &&
                supplier.AdditionalNumber is null && supplier.DueDays == 0,
                "PUR01 optional or required fields have wrong defaults");
            return Task.CompletedTask;
        });
        test("PUR01: missing linked references, audit timestamp and negative due days reject", f =>
        {
            var beforeAudit = f.Store.Count<AuditEvent>();
            var beforeOutbox = f.Store.Count<OutboxEvent>();
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            Supplier Make(Guid account, Guid currency, Guid country, int days, DateTimeOffset when) =>
                new(f.CompanyId, "SUP-10", "مورد", "مورد قانوني", account, currency,
                    "نقدا", days, country, "صنعاء", "شارع", true, f.Owner.Subject, when);
            Reject("supplier_reference_required", () => Make(Guid.Empty, id, id, 0, now));
            Reject("supplier_reference_required", () => Make(id, Guid.Empty, id, 0, now));
            Reject("supplier_reference_required", () => Make(id, id, Guid.Empty, 0, now));
            Reject("supplier_due_days_invalid", () => Make(id, id, id, -1, now));
            Reject("supplier_audit_date_required", () => Make(id, id, id, 0, default));
            Reject("company_required", () => new Supplier(Guid.Empty, "SUP-10", "مورد",
                "قانوني", id, id, "نقدا", 0, id, "صنعاء", "شارع", true, f.Owner.Subject, now));
            Ensure(f.Store.Count<AuditEvent>() == beforeAudit &&
                f.Store.Count<OutboxEvent>() == beforeOutbox,
                "PUR01 rejected card mutated audit or outbox");
            return Task.CompletedTask;
        });
        test("PUR01: required text, malformed code and control characters reject", f =>
        {
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            Supplier Make(string code, string name, string legal, string terms, string city, string street,
                string actor, string? iban = null) =>
                new(f.CompanyId, code, name, legal, id, id, terms, 0, id, city, street,
                    true, actor, now, iban: iban);
            Reject("invalid_code", () => Make("رمز عربي", "مورد", "قانوني", "نقدا", "صنعاء", "شارع", "actor"));
            Reject("invalid_text", () => Make("SUP-1", "", "قانوني", "نقدا", "صنعاء", "شارع", "actor"));
            Reject("invalid_text", () => Make("SUP-1", "مورد", "", "نقدا", "صنعاء", "شارع", "actor"));
            Reject("invalid_text", () => Make("SUP-1", "مورد", "قانوني", "", "صنعاء", "شارع", "actor"));
            Reject("invalid_text", () => Make("SUP-1", "مورد", "قانوني", "نقدا", "", "شارع", "actor"));
            Reject("invalid_text", () => Make("SUP-1", "مورد", "قانوني", "نقدا", "صنعاء", "", "actor"));
            Reject("invalid_text", () => Make("SUP-1", "مورد", "قانوني", "نقدا", "صنعاء", "شارع", ""));
            Reject("invalid_text", () => Make("SUP-1", "مورد", "قانوني", "نقدا", "صنعاء", "شارع", "actor", "\u0001bad"));
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException exception) when (exception.Code == code) { return; }
        throw new Exception("PUR01 expected rejection: " + code);
    }

    private static void Ensure(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }
}
