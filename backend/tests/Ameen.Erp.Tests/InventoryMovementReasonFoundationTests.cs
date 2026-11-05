using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// INV16-FG01..FG04 are structural only, not authority for a movement reason policy.
public static class InventoryMovementReasonFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("INV16-FG01..FG04: tenant branch direction reference and generated audit field fidelity", f =>
        {
            var now = new FixedClock().UtcNow;
            var route = Guid.NewGuid();
            var reason = new InventoryMovementReason(f.CompanyId, f.BranchId, "TEST_STATUS",
                "TEST-REASON", "سبب اختباري", "TEST_DIRECTION", f.EquityId, true,
                route, " test-actor ", now);
            Ensure(reason.CompanyId == f.CompanyId && reason.BranchId == f.BranchId &&
                reason.Id != Guid.Empty && reason.Version != Guid.Empty &&
                reason.StatusCode == "TEST_STATUS" && reason.ReasonCode == "TEST-REASON" &&
                reason.Description == "سبب اختباري" && reason.DirectionCode == "TEST_DIRECTION" &&
                reason.CounterAccountId == f.EquityId && reason.RequireCostCenter &&
                reason.ApprovalRouteId == route && reason.CreatedBy == "test-actor" &&
                reason.CreatedAt == now && reason.LastModifiedAt == now &&
                reason.Revision == 1, "INV16 domain field mismatch");
            var allBranch = new InventoryMovementReason(f.CompanyId, null, "TEST_STATUS",
                "TEST-OTHER", "وصف", "TEST_DIRECTION", null, false,
                route, f.Owner.Subject, now);
            Ensure(allBranch.BranchId is null && allBranch.CounterAccountId is null &&
                !allBranch.RequireCostCenter, "INV16 conditional references changed");
            Ensure(f.Store.Count<JournalEntry>() == 0 && f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "INV16 field construction created effects");
            return Task.CompletedTask;
        });
        test("INV16-01: empty tenant, references and missing audit fields rejected", f =>
        {
            var now = new FixedClock().UtcNow;
            var route = Guid.NewGuid();
            InventoryMovementReason Make(Guid company, Guid? branch, Guid? account,
                Guid approval, string actor, DateTimeOffset time) =>
                new(company, branch, "TEST_STATUS", "TEST", "وصف",
                    "TEST_DIRECTION", account, false, approval, actor, time);
            Reject("company_required", () => Make(Guid.Empty, f.BranchId, null, route, "actor", now));
            Reject("invalid_movement_reason_reference", () => Make(f.CompanyId, Guid.Empty, null, route, "actor", now));
            Reject("invalid_movement_reason_reference", () => Make(f.CompanyId, null, Guid.Empty, route, "actor", now));
            Reject("movement_reason_approval_required", () => Make(f.CompanyId, null, null, Guid.Empty, "actor", now));
            Reject("movement_reason_audit_time_required", () => Make(f.CompanyId, null, null, route, "actor", default));
            Reject("invalid_text", () => Make(f.CompanyId, null, null, route, " ", now));
            Ensure(f.Store.Count<StockMovement>() == 0 && f.Store.Count<JournalEntry>() == 0,
                "INV16 invalid domain mutated ledger");
            return Task.CompletedTask;
        });
        test("INV16-FG01/FG02: opaque direction and status are only shape-checked, never approved", f =>
        {
            var route = Guid.NewGuid();
            var now = new FixedClock().UtcNow;
            InventoryMovementReason Make(string status, string code, string description, string direction) =>
                new(f.CompanyId, null, status, code, description, direction, null,
                    false, route, f.Owner.Subject, now);
            Reject("invalid_text", () => Make("", "TEST", "وصف", "TEST_DIRECTION"));
            Reject("invalid_code", () => Make("test/status", "TEST", "وصف", "TEST_DIRECTION"));
            Reject("invalid_code", () => Make("TEST_STATUS", "bad code", "وصف", "TEST_DIRECTION"));
            Reject("invalid_text", () => Make("TEST_STATUS", "TEST", "", "TEST_DIRECTION"));
            Reject("invalid_code", () => Make("TEST_STATUS", "TEST", "وصف", "bad direction"));
            Reject("invalid_text", () => Make("TEST_STATUS", "TEST", "وصف", ""));
            Ensure(f.Store.Count<InventoryMovementReason>() == 0, "INV16 generated a live policy");
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("INV16 expected " + code);
    }
    private static void Ensure(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
