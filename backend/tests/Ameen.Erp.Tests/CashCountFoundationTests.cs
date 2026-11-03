using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH11-01 only: no approved balance/reconciliation workflow is asserted.
public static class CashCountFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH11-01: original header fields and nullable branch preserve cutoff and audit", f =>
        {
            var now = new FixedClock().UtcNow;
            var count = new CashCount(f.CompanyId, f.BranchId, "Unreviewed",
                Guid.NewGuid(), now.AddMinutes(-1), Guid.NewGuid(), f.Owner.Subject, now);
            Ensure(count.Id != Guid.Empty && count.CompanyId == f.CompanyId &&
                count.BranchId == f.BranchId && count.State == "Unreviewed" &&
                count.CutoffAt == now.AddMinutes(-1) && count.CreatedAt == now &&
                count.LastModifiedAt == now && count.Revision == 1);
            var companyWide = new CashCount(f.CompanyId, null, "Unreviewed",
                Guid.NewGuid(), now, Guid.NewGuid(), f.Owner.Subject, now);
            Ensure(companyWide.BranchId is null && companyWide.Version != Guid.Empty);
            return Task.CompletedTask;
        });

        test("CASH11-01: denomination multiplied by pieces is exactly decimal(20,6)", f =>
        {
            var line = new CashCountLine(f.CompanyId, Guid.NewGuid(), 0.000001m, 2000000);
            Ensure(line.Denomination == 0.000001m && line.PieceCount == 2000000 &&
                line.CountedValue == 2.000000m);
            var high = new CashCountLine(f.CompanyId, Guid.NewGuid(), 99999999999999m, 1);
            Ensure(high.CountedValue == 99999999999999m);
            return Task.CompletedTask;
        });

        test("CASH11-01: invalid count references, negative pieces and money precision are rejected", f =>
        {
            var now = new FixedClock().UtcNow;
            Reject("cash_count_branch_invalid", () => new CashCount(f.CompanyId, Guid.Empty,
                "Unreviewed", Guid.NewGuid(), now, Guid.NewGuid(), f.Owner.Subject, now));
            Reject("cash_count_reference_required", () => new CashCount(f.CompanyId, null,
                "Unreviewed", Guid.Empty, now, Guid.NewGuid(), f.Owner.Subject, now));
            Reject("invalid_text", () => new CashCount(f.CompanyId, null,
                " ", Guid.NewGuid(), now, Guid.NewGuid(), f.Owner.Subject, now));
            Reject("cash_count_reference_required", () =>
                new CashCountLine(f.CompanyId, Guid.Empty, 1m, 1));
            Reject("invalid_amount", () =>
                new CashCountLine(f.CompanyId, Guid.NewGuid(), 0m, 1));
            Reject("invalid_amount", () =>
                new CashCountLine(f.CompanyId, Guid.NewGuid(), 0.0000001m, 1));
            Reject("cash_count_piece_count_invalid", () =>
                new CashCountLine(f.CompanyId, Guid.NewGuid(), 1m, -1));
            Reject("invalid_amount", () =>
                new CashCountLine(f.CompanyId, Guid.NewGuid(), 99999999999999m, 2));
            return Task.CompletedTask;
        });

        test("CASH11-01: structural count and calculated lines do not adjust drawer or ledger", f =>
        {
            var now = new FixedClock().UtcNow;
            var count = new CashCount(f.CompanyId, null, "Unreviewed",
                Guid.NewGuid(), now, Guid.NewGuid(), f.Owner.Subject, now);
            var line = new CashCountLine(f.CompanyId, count.Id, 100m, 5);
            Ensure(line.CountedValue == 500m && line.CashCountId == count.Id &&
                f.Store.All<CashBox>().Count() == 0 &&
                f.Store.All<JournalEntry>().Count() == 0);
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action act)
    {
        try { act(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("CASH11 expected " + code);
    }

    private static void Ensure(bool ok)
    {
        if (!ok) throw new Exception("CASH11 foundation assertion failed");
    }
}
