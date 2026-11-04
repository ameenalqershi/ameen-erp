using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SAL04 domain-only tests, not authorization or reservation acceptance.
public static class SalesOrderFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SAL04-FG: order header, quotation reference and lines retain original fields", f =>
        {
            var date = new DateOnly(2026, 11, 5);
            var at = new FixedClock().UtcNow;
            var year = Guid.NewGuid();
            var currency = Guid.NewGuid();
            var quote = Guid.NewGuid();
            var customer = Guid.NewGuid();
            var item = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var tax = Guid.NewGuid();
            var order = new SalesOrder(f.CompanyId, f.BranchId, year, "00045",
                date, date, currency, 1.23456789m, "وصف", "EXT-01",
                "opaque", quote, customer, date.AddDays(3), "صافي 30",
                new[] { (item, unit, 2.123456m, 15.123456m,
                    (decimal?)0.001m, (Guid?)tax) }, f.Owner.Subject, at);
            Ensure(order.Id != Guid.Empty && order.CompanyId == f.CompanyId &&
                order.BranchId == f.BranchId && order.FiscalYearId == year &&
                order.DocumentNumber == "00045" && order.DocumentDate == date &&
                order.EffectDate == date && order.CurrencyId == currency &&
                order.ExchangeRate == 1.23456789m && order.Description == "وصف" &&
                order.ExternalReference == "EXT-01" && order.Status == "opaque" &&
                order.SourceQuotationId == quote && order.CustomerId == customer &&
                order.DeliveryDate == date.AddDays(3) && order.PaymentTerms == "صافي 30" &&
                order.CreatedBy == f.Owner.Subject && order.CreatedAt == at &&
                order.LastModifiedAt == at && order.Revision == 1,
                "SAL04 order header mismatch");
            var line = order.Lines.Single();
            Ensure(line.OrderId == order.Id && line.CompanyId == f.CompanyId &&
                line.Position == 1 && line.ItemId == item && line.UnitId == unit &&
                line.Quantity == 2.123456m && line.Price == 15.123456m &&
                line.Discount == 0.001m && line.TaxCodeId == tax,
                "SAL04 order line mismatch");
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL04 construction made reservation or accounting effects");
            return Task.CompletedTask;
        });

        test("SAL04-FG: direct order needs no source quotation or tax reference", f =>
        {
            var order = Make(f);
            Ensure(order.SourceQuotationId is null &&
                order.ExternalReference is null &&
                order.Lines.Single().Discount is null &&
                order.Lines.Single().TaxCodeId is null,
                "SAL04 optional source/line fields lost");
            return Task.CompletedTask;
        });

        test("SAL04-AC02: invalid reference, date, FX and line amounts reject", f =>
        {
            Reject("sal04_reference_required", () => Make(f, branch: Guid.Empty));
            Reject("sal04_reference_required", () => Make(f, customer: Guid.Empty));
            Reject("sal04_reference_required", () => Make(f, source: Guid.Empty));
            Reject("sal04_dates_required", () => Make(f, delivery: default(DateOnly)));
            Reject("sal04_audit_date_required", () => Make(f, audit: default(DateTimeOffset)));
            Reject("sal04_exchange_rate_invalid", () => Make(f, rate: -1m));
            Reject("sal04_exchange_rate_invalid", () => Make(f, rate: 0m));
            Reject("sal04_line_reference_required", () => Make(f, item: Guid.Empty));
            Reject("sal04_line_reference_required", () => Make(f, tax: Guid.Empty));
            Reject("invalid_amount", () => Make(f, quantity: 0m));
            Reject("invalid_amount", () => Make(f, quantity: 0.0000001m));
            Reject("invalid_amount", () => Make(f, price: -.000001m));
            Reject("invalid_amount", () => Make(f, discount: -.000001m));
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL04 rejection had side effects");
            return Task.CompletedTask;
        });
    }

    private static SalesOrder Make(Fixture f, Guid? branch = null,
        Guid? customer = null, Guid? source = null, DateOnly? delivery = null,
        DateTimeOffset? audit = null, decimal rate = 1m,
        Guid? item = null, Guid? tax = null, decimal quantity = 1m,
        decimal price = 1m, decimal? discount = null)
    {
        var date = new DateOnly(2026, 11, 5);
        return new SalesOrder(f.CompanyId, branch ?? f.BranchId, Guid.NewGuid(),
            "ORD-1", date, date, Guid.NewGuid(), rate, "order", null,
            "opaque", source, customer ?? Guid.NewGuid(), delivery ?? date,
            "terms", new[] { (item ?? Guid.NewGuid(), Guid.NewGuid(), quantity,
                price, discount, tax) }, f.Owner.Subject,
            audit ?? new FixedClock().UtcNow);
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("SAL04 expected " + code);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
