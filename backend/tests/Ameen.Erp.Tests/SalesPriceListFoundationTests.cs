using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SAL02 data-contract acceptance only; no price selection, approval or invoice impact.
public static class SalesPriceListFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SAL02-FG: price list header/lines retain exact references, decimals and dates", f =>
        {
            var at = new FixedClock().UtcNow;
            var currency = Guid.NewGuid();
            var item = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var from = new DateOnly(2026, 11, 5);
            var to = new DateOnly(2027, 11, 5);
            var priceList = new SalesPriceList(f.CompanyId, f.BranchId, "uninterpreted",
                "قائمة أسعار", currency, from, to, true,
                new[] { (item, unit, 12.123456m, 0.000001m, (decimal?)2.5m) },
                f.Owner.Subject, at);
            var line = priceList.Lines.Single();
            Ensure(priceList.CompanyId == f.CompanyId && priceList.Id != Guid.Empty &&
                priceList.BranchId == f.BranchId && priceList.CurrencyId == currency &&
                priceList.Status == "uninterpreted" && priceList.Name == "قائمة أسعار" &&
                priceList.EffectiveFrom == from && priceList.EffectiveTo == to &&
                priceList.IncludesTax && priceList.CreatedBy == f.Owner.Subject &&
                priceList.CreatedAt == at && priceList.LastModifiedAt == at &&
                priceList.Revision == 1 && line.PriceListId == priceList.Id &&
                line.CompanyId == f.CompanyId && line.Position == 1 &&
                line.ItemId == item && line.UnitId == unit &&
                line.Price == 12.123456m && line.DiscountLimit == 0.000001m &&
                line.QuantityThreshold == 2.5m, "SAL02 structural field loss");
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL02 created accounting side effects");
            return Task.CompletedTask;
        });

        test("SAL02-FG: nullable branch, end date and quantity remain optional", f =>
        {
            var priceList = new SalesPriceList(f.CompanyId, null, "pending",
                "Standalone", Guid.NewGuid(), new DateOnly(2026, 11, 5),
                null, false, new[]
                {
                    (Guid.NewGuid(), Guid.NewGuid(), 0m, 0m, (decimal?)null)
                }, f.Owner.Subject, new FixedClock().UtcNow);
            Ensure(priceList.BranchId is null && priceList.EffectiveTo is null &&
                !priceList.IncludesTax && priceList.Lines[0].QuantityThreshold is null &&
                priceList.Lines[0].Price == 0m, "SAL02 optional field mismatch");
            return Task.CompletedTask;
        });

        test("SAL02-AC02: invalid references, precision, dates or audit are rejected", f =>
        {
            var currency = Guid.NewGuid();
            var item = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var date = new DateOnly(2026, 11, 5);
            var at = new FixedClock().UtcNow;
            SalesPriceList Make(Guid? branch, Guid cur, DateOnly start, DateOnly? end,
                Guid lineItem, Guid lineUnit, decimal price, decimal discount,
                decimal? quantity, DateTimeOffset audit, string status = "opaque") =>
                new(f.CompanyId, branch, status, "List", cur, start, end, false,
                    new[] { (lineItem, lineUnit, price, discount, quantity) },
                    f.Owner.Subject, audit);
            Reject("sal02_reference_required", () =>
                Make(Guid.Empty, currency, date, null, item, unit, 1, 0, null, at));
            Reject("sal02_reference_required", () =>
                Make(null, Guid.Empty, date, null, item, unit, 1, 0, null, at));
            Reject("sal02_line_reference_required", () =>
                Make(null, currency, date, null, Guid.Empty, unit, 1, 0, null, at));
            Reject("sal02_line_reference_required", () =>
                Make(null, currency, date, null, item, Guid.Empty, 1, 0, null, at));
            Reject("sal02_effective_dates_invalid", () =>
                Make(null, currency, default, null, item, unit, 1, 0, null, at));
            Reject("sal02_effective_dates_invalid", () =>
                Make(null, currency, date, date.AddDays(-1), item, unit, 1, 0, null, at));
            Reject("sal02_audit_date_required", () =>
                Make(null, currency, date, null, item, unit, 1, 0, null, default));
            Reject("invalid_amount", () =>
                Make(null, currency, date, null, item, unit, -1, 0, null, at));
            Reject("invalid_amount", () =>
                Make(null, currency, date, null, item, unit, .0000001m, 0, null, at));
            Reject("invalid_amount", () =>
                Make(null, currency, date, null, item, unit, 1, -1, null, at));
            Reject("invalid_amount", () =>
                Make(null, currency, date, null, item, unit, 1, 0, -.000001m, at));
            Reject("invalid_text", () =>
                Make(null, currency, date, null, item, unit, 1, 0, null, at, " "));
            Reject("sal02_lines_required", () =>
                new SalesPriceList(f.CompanyId, null, "opaque", "List", currency,
                    date, null, false,
                    Array.Empty<(Guid, Guid, decimal, decimal, decimal?)>(),
                    f.Owner.Subject, at));
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL02 rejection had financial effect");
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("SAL02 expected " + code);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
