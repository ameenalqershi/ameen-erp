using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH06-01 schema/domain fidelity only; NOT financial acceptance.
public static class CashAdvanceFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH06-01: preserve 23 original header/line/audit fields without financial effect", f =>
        {
            var now = new FixedClock().UtcNow;
            var doc = new DateOnly(2026, 11, 3);
            var expected = doc.AddDays(15);
            var advance = new CashAdvance(f.CompanyId, f.BranchId, Guid.NewGuid(),
                "000012", doc, doc, "SAR", 1.2345678901m,
                "Advance purpose", "EXT-0001", "Pending", Guid.NewGuid(),
                "UnapprovedType", 75.123456m, expected, f.Owner.Subject, now);
            var line = new CashAdvanceLine(f.CompanyId, advance.Id,
                Guid.NewGuid(), 12.000001m, "invoice-0001");
            Ensure(advance.CompanyId == f.CompanyId && advance.BranchId == f.BranchId &&
                advance.Number == "000012" && advance.DocumentDate == doc &&
                advance.AccountingDate == doc && advance.CurrencyCode == "SAR" &&
                advance.ExchangeRate == 1.2345678901m &&
                advance.Description == "Advance purpose" &&
                advance.ExternalReference == "EXT-0001" &&
                advance.State == "Pending" && advance.HolderId != Guid.Empty &&
                advance.AdvanceType == "UnapprovedType" &&
                advance.Amount == 75.123456m &&
                advance.ExpectedSettlementDate == expected &&
                advance.Revision == 1 && advance.CreatedBy == f.Owner.Subject &&
                advance.CreatedAt == now && advance.LastModifiedAt == now &&
                line.AdvanceId == advance.Id && line.CompanyId == f.CompanyId &&
                line.ExpenseReferenceId.HasValue &&
                line.SettlementAmount == 12.000001m &&
                line.AttachmentReference == "invoice-0001");
            Ensure(f.Store.All<JournalEntry>().Count() == 0 &&
                f.Store.All<CashBox>().Count() == 0);
            return Task.CompletedTask;
        });

        test("CASH06-01: optional settlement fields do not imply approved policy", f =>
        {
            var line = new CashAdvanceLine(f.CompanyId, Guid.NewGuid(), null, null, null);
            Ensure(line.ExpenseReferenceId == null &&
                line.SettlementAmount == null && line.AttachmentReference == null);
            var zero = new CashAdvanceLine(f.CompanyId, Guid.NewGuid(),
                null, 0m, "scan");
            Ensure(zero.SettlementAmount == 0m);
            return Task.CompletedTask;
        });

        test("CASH06-01: invalid references, dates, rates and cash amounts fail atomically", f =>
        {
            var now = new FixedClock().UtcNow;
            var date = new DateOnly(2026, 11, 3);
            CashAdvance Make(Guid branch, Guid year, Guid holder, DateOnly dateValue,
                decimal rate, decimal amount) =>
                new(f.CompanyId, branch, year, "001", dateValue, date,
                    "SAR", rate, "purpose", null, "Pending", holder, "Type",
                    amount, date, f.Owner.Subject, now);
            Reject("cash_advance_reference_required",
                () => Make(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), date, 1m, 1m));
            Reject("cash_advance_reference_required",
                () => Make(f.BranchId, Guid.Empty, Guid.NewGuid(), date, 1m, 1m));
            Reject("cash_advance_reference_required",
                () => Make(f.BranchId, Guid.NewGuid(), Guid.Empty, date, 1m, 1m));
            Reject("cash_advance_date_required",
                () => Make(f.BranchId, Guid.NewGuid(), Guid.NewGuid(), default, 1m, 1m));
            Reject("invalid_exchange_rate",
                () => Make(f.BranchId, Guid.NewGuid(), Guid.NewGuid(), date, 0m, 1m));
            Reject("invalid_exchange_rate",
                () => Make(f.BranchId, Guid.NewGuid(), Guid.NewGuid(), date, 1.00000000001m, 1m));
            Reject("invalid_amount",
                () => Make(f.BranchId, Guid.NewGuid(), Guid.NewGuid(), date, 1m, 0m));
            Reject("invalid_amount",
                () => Make(f.BranchId, Guid.NewGuid(), Guid.NewGuid(), date, 1m, 0.0000001m));
            Reject("cash_advance_line_reference_invalid",
                () => new CashAdvanceLine(f.CompanyId, Guid.Empty, null, 1m, null));
            Reject("cash_advance_line_reference_invalid",
                () => new CashAdvanceLine(f.CompanyId, Guid.NewGuid(), Guid.Empty, 1m, null));
            Reject("invalid_amount",
                () => new CashAdvanceLine(f.CompanyId, Guid.NewGuid(), null, -1m, null));
            Reject("invalid_amount",
                () => new CashAdvanceLine(f.CompanyId, Guid.NewGuid(), null, 0.0000001m, null));
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("CASH06 should reject with " + code);
    }

    private static void Ensure(bool condition)
    {
        if (!condition) throw new Exception("CASH06 foundation assertion failed");
    }
}
