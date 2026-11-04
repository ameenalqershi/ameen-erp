using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SYS16-01 is only a domain/schema foundation. No payment is posted or collected.
public static class PaymentTermFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS16-01: cash, card and credit payment cards retain tenant and audit fields", f =>
        {
            var now = new FixedClock().UtcNow;
            foreach (var kind in Enum.GetValues<PaymentMethodKind>())
            {
                var card = Create(f, kind, new[] { (20m, 0), (30m, 14), (50m, 30) });
                Ensure(card.CompanyId == f.CompanyId && card.BranchId == f.BranchId &&
                    card.Id != Guid.Empty && card.Code == "PM-01" && card.Name == "طريقة دفع" &&
                    card.Kind == kind && card.SettlementAccountId == f.CashId &&
                    card.GraceDays == 7 && card.IsActive && card.Revision == 1 &&
                    card.CreatedBy == f.Owner.Subject && card.CreatedAt == now && card.LastModifiedAt == now &&
                    card.Lines.Count == 3 && card.Lines.Sum(x => x.Percent) == 100m &&
                    card.Lines.Select(x => x.Position).SequenceEqual(new[] { 1, 2, 3 }) &&
                    card.Lines.Select(x => x.DaysFromBase).SequenceEqual(new[] { 0, 14, 30 }) &&
                    card.Lines.All(x => x.TermId == card.Id && x.CompanyId == f.CompanyId));
                var oldVersion=card.Version;
                card.SetActive(false, now);
                Ensure(!card.IsActive && card.Revision == 2 && card.Version != oldVersion &&
                    card.Lines.Sum(x => x.Percent)==100m);
            }
            return Task.CompletedTask;
        });
        test("SYS16-FG03: decimal installment portions require exact 100 percent", f =>
        {
            var card = Create(f, PaymentMethodKind.Credit, new[]
                { (33.333333m, 0), (33.333333m, 30), (33.333334m, 60) });
            Ensure(card.Lines.Sum(x => x.Percent) == 100m);
            Error("payment_plan_total_invalid", () => Create(f, PaymentMethodKind.Credit,
                new[] { (33.333333m, 0), (33.333333m, 30), (33.333333m, 60) }));
            Error("payment_plan_total_invalid", () => Create(f, PaymentMethodKind.Credit,
                new[] { (70m, 0), (31m, 30) }));
            Error("payment_percentage_invalid", () => Create(f, PaymentMethodKind.Credit,
                new[] { (0m, 0), (100m, 30) }));
            Error("payment_percentage_invalid", () => Create(f, PaymentMethodKind.Credit,
                new[] { (50.0000001m, 0), (49.9999999m, 30) }));
            Error("payment_days_invalid", () => Create(f, PaymentMethodKind.Credit,
                new[] { (50m, -1), (50m, 30) }));
            return Task.CompletedTask;
        });
        test("SYS16-01: optional settlement and branch remain nullable without invented policy", f =>
        {
            var c = new PaymentTerm(f.CompanyId, null, "0007", "دفع", PaymentMethodKind.Card,
                null, 0, Array.Empty<(decimal, int)>(), f.Owner.Subject, new FixedClock().UtcNow);
            Ensure(c.BranchId is null && c.SettlementAccountId is null &&
                c.Code == "0007" && c.Lines.Count == 0);
            Error("payment_term_reference_invalid", () => new PaymentTerm(f.CompanyId,
                Guid.Empty, "0007", "دفع", PaymentMethodKind.Card, null, 0,
                null, f.Owner.Subject, new FixedClock().UtcNow));
            Error("payment_method_kind_invalid", () => Create(f, (PaymentMethodKind)99, null));
            Error("payment_grace_days_invalid", () => new PaymentTerm(f.CompanyId,
                null, "0007", "دفع", PaymentMethodKind.Cash, null, -1,
                null, f.Owner.Subject, new FixedClock().UtcNow));
            Error("invalid_text", () => new PaymentTerm(f.CompanyId,
                null, "0007", " ", PaymentMethodKind.Cash, null, 0,
                null, f.Owner.Subject, new FixedClock().UtcNow));
            Ensure(f.Store.Count<PaymentTerm>() == 0);
            return Task.CompletedTask;
        });
        test("SYS16-FG04: default creation timestamp is rejected before payment card persistence", f =>
        {
            var beforeAudit=f.Store.Count<AuditEvent>();
            var beforeJournal=f.Store.Count<JournalEntry>();
            foreach (var kind in Enum.GetValues<PaymentMethodKind>())
                Error("payment_term_audit_time_invalid", () =>
                    new PaymentTerm(f.CompanyId, f.BranchId, "PM-01", "طريقة دفع",
                        kind, f.CashId, 7, new[] { (100m, 0) },
                        f.Owner.Subject, default));
            Ensure(f.Store.Count<PaymentTerm>() == 0 &&
                f.Store.Count<PaymentTermLine>() == 0 &&
                f.Store.Count<AuditEvent>() == beforeAudit &&
                f.Store.Count<JournalEntry>() == beforeJournal);
            return Task.CompletedTask;
        });
        test("SYS16-01: audit revision cannot overflow or time-travel on status change", f =>
        {
            var card=Create(f,PaymentMethodKind.Cash,null);
            var version=card.Version;
            var modified=card.LastModifiedAt;
            Error("payment_term_audit_time_invalid", () => card.SetActive(false,modified.AddDays(-1)));
            Ensure(card.Revision==1 && card.Version==version && card.IsActive);
            typeof(PaymentTerm).GetProperty(nameof(PaymentTerm.Revision))!
                .SetValue(card,int.MaxValue);
            Error("payment_term_revision_exhausted", () => card.SetActive(false,modified));
            Ensure(card.IsActive && card.Version==version && card.Revision==int.MaxValue);
            return Task.CompletedTask;
        });
        test("SYS16-AC02: card configuration never creates a cash receipt or ledger effect", f =>
        {
            var beforeAudit=f.Store.Count<AuditEvent>();
            var beforeJournal=f.Store.Count<JournalEntry>();
            var card=Create(f,PaymentMethodKind.Card,null);
            Ensure(card.Kind==PaymentMethodKind.Card && f.Store.Count<CashBox>()==0 &&
                f.Store.Count<JournalEntry>()==beforeJournal &&
                f.Store.Count<AuditEvent>()==beforeAudit);
            return Task.CompletedTask;
        });
    }

    private static PaymentTerm Create(Fixture f, PaymentMethodKind kind,
        IReadOnlyList<(decimal, int)>? schedule) =>
        new(f.CompanyId, f.BranchId, "PM-01", "طريقة دفع", kind, f.CashId, 7,
            schedule, f.Owner.Subject, new FixedClock().UtcNow);

    private static void Error(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when(e.Code==code) { return; }
        throw new Exception("Expected SYS16 domain error " + code);
    }

    private static void Ensure(bool condition)
    {
        if (!condition) throw new Exception("SYS16 domain foundation assertion failed");
    }
}
