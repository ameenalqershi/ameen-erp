using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CurrencyRevaluationPreviewPolicyTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL08-C01 partial: exact decimal preview of verified monetary items", f =>
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var result = CurrencyRevaluationPreviewPolicy.Evaluate(new[]
            {
                new RevaluationOpenItem(first, true, 120.125m, 121.375m),
                new RevaluationOpenItem(second, true, 40.500m, 39.250m)
            });
            Ensure(result.Status == RevaluationPreviewStatus.Valid &&
                   result.Differences.Count == 2 &&
                   result.Differences[0] == new RevaluationDifference(first, 1.250m) &&
                   result.Differences[1] == new RevaluationDifference(second, -1.250m) &&
                   !result.MayPost, "GL08 preview arithmetic or posting gate failed");
            return Task.CompletedTask;
        });
        test("GL08-C01 partial: historical/ineligible asset cannot enter preview", f =>
        {
            var result = CurrencyRevaluationPreviewPolicy.Evaluate(new[]
            {
                new RevaluationOpenItem(Guid.NewGuid(), true, 1m, 2m),
                new RevaluationOpenItem(Guid.NewGuid(), false, 100m, 101m)
            });
            Ensure(result.Status == RevaluationPreviewStatus.IneligibleItem &&
                   result.Differences.Count == 0 && !result.MayPost,
                "GL08 ineligible item leaked a partial monetary preview");
            return Task.CompletedTask;
        });
        test("GL08-C01 partial: duplicate and invalid open-item references fail closed", f =>
        {
            var id = Guid.NewGuid();
            var duplicated = CurrencyRevaluationPreviewPolicy.Evaluate(new[]
            {
                new RevaluationOpenItem(id, true, 2m, 3m),
                new RevaluationOpenItem(id, true, 3m, 4m)
            });
            Ensure(duplicated.Status == RevaluationPreviewStatus.DuplicateOpenItem &&
                   duplicated.Differences.Count == 0,
                "GL08 duplicate item was accepted");
            Ensure(CurrencyRevaluationPreviewPolicy.Evaluate(null).Status ==
                       RevaluationPreviewStatus.InvalidSnapshot &&
                   CurrencyRevaluationPreviewPolicy.Evaluate(
                       Array.Empty<RevaluationOpenItem>()).Status ==
                       RevaluationPreviewStatus.InvalidSnapshot &&
                   CurrencyRevaluationPreviewPolicy.Evaluate(new[]
                   {
                       new RevaluationOpenItem(Guid.Empty, true, 0m, 0m)
                   }).Status == RevaluationPreviewStatus.InvalidSnapshot,
                "GL08 missing snapshot/item was accepted");
            return Task.CompletedTask;
        });
        test("GL08-C01 partial: overflow rejects entire preview without rounding", f =>
        {
            var result = CurrencyRevaluationPreviewPolicy.Evaluate(new[]
            {
                new RevaluationOpenItem(Guid.NewGuid(), true, 2m, 3m),
                new RevaluationOpenItem(Guid.NewGuid(), true, decimal.MinValue, decimal.MaxValue)
            });
            Ensure(result.Status == RevaluationPreviewStatus.ArithmeticOverflow &&
                   result.Differences.Count == 0 && !result.MayPost,
                "GL08 overflow yielded partial differences or posting permission");
            return Task.CompletedTask;
        });
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
