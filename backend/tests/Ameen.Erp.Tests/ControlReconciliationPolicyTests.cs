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
            Ensure(ControlReconciliationPolicy.Evaluate(decimal.MaxValue,
                       decimal.MaxValue, null) == ControlReconciliationStatus.Matched,
                "GL10 maximum equal decimal balances overflowed or diverged");
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
            Ensure(ControlReconciliationPolicy.Evaluate(decimal.MaxValue,
                       decimal.MinValue, null) ==
                           ControlReconciliationStatus.UnexplainedDifference,
                "GL10 decimal boundary difference did not fail closed");
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
