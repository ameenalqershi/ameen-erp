using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// Only structural assertions: no catalogue codes are approved in SYS11.
public static class GeneralOptionsFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS11-01: isolated general-options schema retains fields, tenant and audit", f =>
        {
            var now = new FixedClock().UtcNow;
            var model = new GeneralOptionsPolicy(f.CompanyId, f.BranchId, "test-only",
                "unapproved-valuation", true, "unapproved-delivery", 4, f.Owner.Subject,
                now, new[] { ("test-event", "test-role", f.CashId, "test-scope") });
            Ensure(model.CompanyId == f.CompanyId && model.BranchId == f.BranchId &&
                model.StatusCode == "test-only" &&
                model.InventoryValuationCode == "unapproved-valuation" &&
                model.SalesDeliveryModeCode == "unapproved-delivery" &&
                model.BlockNegativeStock && model.QuantityPrecision == 4 &&
                model.CreatedBy == f.Owner.Subject && model.CreatedAt == now &&
                model.LastModifiedAt == now && model.Revision == 1 &&
                model.Lines.Count == 1 && model.Lines[0].CompanyId == f.CompanyId &&
                model.Lines[0].PolicyId == model.Id &&
                model.Lines[0].AccountId == f.CashId &&
                model.Lines[0].EventCode == "test-event" &&
                model.Lines[0].AccountRoleCode == "test-role" &&
                model.Lines[0].Scope == "test-scope",
                "SYS11 original fields not preserved");
            Ensure(f.Store.Count<GeneralOptionsPolicy>() == 0 &&
                f.Store.Count<GeneralOptionsAccountLine>() == 0 &&
                f.Store.Count<JournalEntry>() == 0,
                "SYS11 structural card activated a financial workflow");
            return Task.CompletedTask;
        });
        test("SYS11-FG04: default audit creation timestamp rejects policy before linking accounts", f =>
        {
            var beforeAudit = f.Store.Count<AuditEvent>();
            Error("general_options_audit_time_invalid", () => new GeneralOptionsPolicy(
                f.CompanyId, f.BranchId, "test-only", "test-valuation", true, "test-delivery",
                4, f.Owner.Subject, default,
                new[] { ("test-event", "test-role", f.CashId, "test-scope") }));
            Ensure(f.Store.Count<GeneralOptionsPolicy>() == 0 &&
                f.Store.Count<GeneralOptionsAccountLine>() == 0 &&
                f.Store.Count<AuditEvent>() == beforeAudit &&
                f.Store.Count<JournalEntry>() == 0,
                "SYS11 rejected audit timestamp must not mutate policy, accounts or ledger");
            return Task.CompletedTask;
        });
        test("SYS11-01: malformed references, precision and required text fail closed", f =>
        {
            var now = new FixedClock().UtcNow;
            Error("company_required", () => new GeneralOptionsPolicy(Guid.Empty, null,
                "test-only", "valuation", true, "delivery", 0, "actor", now,
                Array.Empty<(string,string,Guid,string)>()));
            Error("general_options_branch_invalid", () => new GeneralOptionsPolicy(
                f.CompanyId, Guid.Empty, "test-only", "valuation", true, "delivery", 0,
                "actor", now, Array.Empty<(string,string,Guid,string)>()));
            Error("general_options_precision_invalid", () => new GeneralOptionsPolicy(
                f.CompanyId, null, "test-only", "valuation", true, "delivery", -1,
                "actor", now, Array.Empty<(string,string,Guid,string)>()));
            Error("general_options_lines_invalid", () => new GeneralOptionsPolicy(
                f.CompanyId, null, "test-only", "valuation", true, "delivery", 0,
                "actor", now, null!));
            Error("general_options_account_invalid", () => new GeneralOptionsAccountLine(
                f.CompanyId, Guid.NewGuid(), "test-event", "test-role", Guid.Empty, "scope"));
            Error("invalid_text", () => new GeneralOptionsAccountLine(
                f.CompanyId, Guid.NewGuid(), "", "test-role", f.CashId, "scope"));
            Ensure(f.Store.Count<GeneralOptionsPolicy>() == 0,
                "Invalid general-options model persisted");
            return Task.CompletedTask;
        });
    }

    private static void Error(string expected, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == expected) { return; }
        throw new Exception("Expected SYS11 error " + expected);
    }
    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
