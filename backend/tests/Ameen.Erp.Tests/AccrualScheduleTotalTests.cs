using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class AccrualScheduleTotalTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL09-C01 partial: exact scheduled sum equals the documented contract total", f =>
        {
            var period1 = Guid.NewGuid();
            var period2 = Guid.NewGuid();
            var snapshot = new[]
            {
                new AccrualSchedulePeriod(period1, 12.345m),
                new AccrualSchedulePeriod(period2, 7.655m)
            };
            Ensure(AccrualScheduleTotalPolicy.Evaluate(20m, snapshot) ==
                       AccrualScheduleTotalStatus.Balanced,
                "GL09 schedules with exact sum were rejected");
            Ensure(AccrualScheduleTotalPolicy.Evaluate(20.001m, snapshot) ==
                       AccrualScheduleTotalStatus.ContractTotalMismatch,
                "GL09 contract mismatch was accepted");
            Ensure(AccrualScheduleTotalPolicy.Evaluate(19.999m, snapshot) ==
                       AccrualScheduleTotalStatus.ContractTotalMismatch,
                "GL09 small underallocation was rounded into a match");
            return Task.CompletedTask;
        });
        test("GL09-C01 partial: duplicate periods and incomplete schedules fail closed", f =>
        {
            var period = Guid.NewGuid();
            var duplicate = new[]
            {
                new AccrualSchedulePeriod(period, 4m),
                new AccrualSchedulePeriod(period, 6m)
            };
            Ensure(AccrualScheduleTotalPolicy.Evaluate(10m, duplicate) ==
                       AccrualScheduleTotalStatus.DuplicatePeriod,
                "GL09 duplicate period was accepted even with balanced total");
            Ensure(AccrualScheduleTotalPolicy.Evaluate(0m, null) ==
                       AccrualScheduleTotalStatus.InvalidSnapshot &&
                   AccrualScheduleTotalPolicy.Evaluate(0m,
                       Array.Empty<AccrualSchedulePeriod>()) ==
                       AccrualScheduleTotalStatus.InvalidSnapshot &&
                   AccrualScheduleTotalPolicy.Evaluate(0m,
                       new[] { new AccrualSchedulePeriod(Guid.Empty, 0m) }) ==
                       AccrualScheduleTotalStatus.InvalidSnapshot,
                "GL09 invalid schedule snapshot was accepted");
            return Task.CompletedTask;
        });
        test("GL09-C01 partial: decimal overflow fails without monetary rounding", f =>
        {
            var snapshot = new[]
            {
                new AccrualSchedulePeriod(Guid.NewGuid(), decimal.MaxValue),
                new AccrualSchedulePeriod(Guid.NewGuid(), 1m)
            };
            Ensure(AccrualScheduleTotalPolicy.Evaluate(decimal.MaxValue, snapshot) ==
                       AccrualScheduleTotalStatus.InvalidSnapshot,
                "GL09 arithmetic overflow was accepted");
            return Task.CompletedTask;
        });
    }

    private static void Ensure(bool value, string error)
    {
        if (!value) throw new Exception(error);
    }
}
