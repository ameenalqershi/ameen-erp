using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class ControlReconciliationPolicyTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL10-C01 partial: matching ledger/subledger balances pass read-only comparison", f =>
        {
            Ensure(ControlReconciliationPolicy.Evaluate(100.015m, 100.015m, null) ==
                       ControlReconciliationStatus.Matched,
                "GL10 exact matching balances were classified as different");
            Ensure(ControlReconciliationPolicy.Evaluate(-100.123456m,
                       -100.123456m, null) == ControlReconciliationStatus.Matched,
                "GL10 exact signed decimal balances diverged");
            return Task.CompletedTask;
        });
        test("GL10-C01 partial: unexplained discrepancy remains a mandatory investigation", f =>
        {
            foreach (var reason in new string?[] { null, "", "   " })
            {
                Ensure(ControlReconciliationPolicy.Evaluate(5m, 0m, reason) ==
                           ControlReconciliationStatus.UnexplainedDifference,
                    "GL10 unexplained difference was silently cleared");
            }
            Ensure(ControlReconciliationPolicy.Evaluate(100.123456m,
                       100.123455m, null) ==
                           ControlReconciliationStatus.UnexplainedDifference,
                "GL10 one-micro-unit difference was silently treated as balanced");
            return Task.CompletedTask;
        });
        test("GL10-FG03: unstorable ledger/detail/difference values cannot be reconciled", f =>
        {
            Ensure(ControlReconciliationPolicy.Evaluate(decimal.MaxValue,
                       decimal.MaxValue, null) == ControlReconciliationStatus.InvalidSnapshot,
                "GL10 overflow-sized equal balances were falsely matched");
            Ensure(ControlReconciliationPolicy.Evaluate(10.0000001m,
                       10.0000001m, "متطابق") == ControlReconciliationStatus.InvalidSnapshot,
                "GL10 overscale matching amounts were treated as reconciled");
            Ensure(ControlReconciliationPolicy.Evaluate(10.0000001m,
                       10m, null) == ControlReconciliationStatus.InvalidSnapshot &&
                   ControlReconciliationPolicy.Evaluate(10m,
                       -10.0000001m, null) == ControlReconciliationStatus.InvalidSnapshot,
                "GL10 one invalid balance was accepted");
            Ensure(ControlReconciliationPolicy.Evaluate(75_000_000_000_000m,
                       -75_000_000_000_000m, null) == ControlReconciliationStatus.InvalidSnapshot,
                "GL10 subtraction outside Decimal(20,6) range was accepted");
            Ensure(ControlReconciliationPolicy.Evaluate(decimal.MaxValue,
                       decimal.MinValue, "تفسير") == ControlReconciliationStatus.InvalidSnapshot,
                "GL10 arithmetic overflow was cleared by a reason");
            return Task.CompletedTask;
        });
        test("GL10-C01 partial: stating a reason cannot hide nonzero discrepancy", f =>
        {
            Ensure(ControlReconciliationPolicy.Evaluate(100m, 95m,
                       "فرق رقابي يحتاج فحص العمليات") ==
                           ControlReconciliationStatus.ExplainedDifferenceStillOpen,
                "GL10 reason incorrectly cleared a numeric mismatch");
            Ensure(ControlReconciliationPolicy.Evaluate(0m, 1m, "Pending review") !=
                           ControlReconciliationStatus.Matched,
                "GL10 negative difference was incorrectly accepted");
            return Task.CompletedTask;
        });
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
