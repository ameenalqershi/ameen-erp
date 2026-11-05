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
        test("GL09-F14/F20: storage precision cannot be silently rounded into a balanced schedule", f =>
        {
            var p1 = Guid.NewGuid();
            var p2 = Guid.NewGuid();
            Ensure(AccrualScheduleTotalPolicy.Evaluate(0.123456m,
                new[] { new AccrualSchedulePeriod(p1, 0.123456m) }) ==
                    AccrualScheduleTotalStatus.Balanced,
                "Decimal(20,6) exact schedule amount should be accepted");
            Ensure(AccrualScheduleTotalPolicy.Evaluate(0.1234567m,
                new[] { new AccrualSchedulePeriod(p1, 0.1234567m) }) ==
                    AccrualScheduleTotalStatus.InvalidSnapshot,
                "Contract total exceeding Decimal(20,6) scale was accepted");
            Ensure(AccrualScheduleTotalPolicy.Evaluate(10m,
                new[]
                {
                    new AccrualSchedulePeriod(p1, 5.0000001m),
                    new AccrualSchedulePeriod(p2, 4.9999999m)
                }) == AccrualScheduleTotalStatus.InvalidSnapshot,
                "Mutually canceling overscale lines were accepted because their sum matched");
            Ensure(AccrualScheduleTotalPolicy.Evaluate(10m,
                new[] { new AccrualSchedulePeriod(p1, 100_000_000_000_000m) }) ==
                    AccrualScheduleTotalStatus.InvalidSnapshot &&
                AccrualScheduleTotalPolicy.Evaluate(100_000_000_000_000m,
                    new[] { new AccrualSchedulePeriod(p1, 10m) }) ==
                    AccrualScheduleTotalStatus.InvalidSnapshot,
                "Amount exceeding Decimal(20,6) integer precision was accepted");
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
