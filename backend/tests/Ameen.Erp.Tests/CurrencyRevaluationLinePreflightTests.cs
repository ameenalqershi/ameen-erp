using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CurrencyRevaluationLinePreflightTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL08-FG03 partial: exact calculated signed difference without currency rounding", f =>
        {
            Ensure(CurrencyRevaluationLinePreflight.Calculate(100.123456m,
                100.223455m, out var positive) == CurrencyRevaluationLineStatus.Valid &&
                positive is not null && positive.BookValue == 100.123456m &&
                positive.NewValue == 100.223455m &&
                positive.Difference == 0.099999m,
                "GL08 computed gain/loss difference was rounded or reversed");
            Ensure(CurrencyRevaluationLinePreflight.Calculate(10.111111m,
                9.111110m, out var negative) == CurrencyRevaluationLineStatus.Valid &&
                negative is not null && negative.Difference == -1.000001m,
                "GL08 negative difference was clamped");
            return Task.CompletedTask;
        });

        test("GL08-FG03 partial: reject amounts outside Decimal(20,6) without silently rounding", f =>
        {
            Ensure(CurrencyRevaluationLinePreflight.Calculate(0.1234567m, 1m, out var invalidBook) ==
                CurrencyRevaluationLineStatus.InvalidBookValue && invalidBook is null,
                "GL08 overprecision book value was accepted");
            Ensure(CurrencyRevaluationLinePreflight.Calculate(0m, 1.0000001m, out var invalidNew) ==
                CurrencyRevaluationLineStatus.InvalidNewValue && invalidNew is null,
                "GL08 overprecision revalued amount was accepted");
            Ensure(CurrencyRevaluationLinePreflight.Calculate(100_000_000_000_000m, 0m, out var hugeBook) ==
                CurrencyRevaluationLineStatus.InvalidBookValue && hugeBook is null,
                "GL08 book value beyond Decimal(20,6) was accepted");
            Ensure(CurrencyRevaluationLinePreflight.Calculate(0m, -100_000_000_000_000m, out var hugeNew) ==
                CurrencyRevaluationLineStatus.InvalidNewValue && hugeNew is null,
                "GL08 new value beyond Decimal(20,6) was accepted");
            return Task.CompletedTask;
        });

        test("GL08-FG03 partial: difference crossing Decimal(20,6) range is rejected", f =>
        {
            const decimal nearLimit = 99_999_999_999_999.999999m;
            Ensure(CurrencyRevaluationLinePreflight.Calculate(-nearLimit,
                nearLimit, out var invalid) == CurrencyRevaluationLineStatus.InvalidDifference &&
                invalid is null, "GL08 aggregate difference exceeded SQL field precision");
            Ensure(CurrencyRevaluationLinePreflight.Calculate(0m, 0m, out var zero) ==
                CurrencyRevaluationLineStatus.Valid &&
                zero is not null && zero.Difference == 0m,
                "GL08 zero difference was rejected");
            return Task.CompletedTask;
        });
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
