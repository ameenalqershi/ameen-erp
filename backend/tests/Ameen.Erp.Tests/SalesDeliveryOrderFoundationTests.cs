using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SAL05 inventory issue and order-line trace are deliberately not enabled.
public static class SalesDeliveryOrderFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SAL05-FG: delivery document retains source fields and batch identity", f =>
        {
            var date = new DateOnly(2026, 11, 5);
            var at = new FixedClock().UtcNow;
            var year = Guid.NewGuid();
            var currency = Guid.NewGuid();
            var customer = Guid.NewGuid();
            var order = Guid.NewGuid();
            var warehouse = Guid.NewGuid();
            var item = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var delivery = new SalesDeliveryOrder(f.CompanyId, f.BranchId,
                year, "00079", date, date, currency, 1.2345678m, "تسليم",
                "EXT-01", "opaque", customer, order, warehouse,
                "مستلم", "SHIP-00",
                new[] { (item, unit, 3.123456m, (string?)"0001"),
                    (Guid.NewGuid(), unit, 1m, (string?)null) },
                f.Owner.Subject, at);
            Ensure(delivery.Id != Guid.Empty && delivery.CompanyId == f.CompanyId &&
                delivery.BranchId == f.BranchId && delivery.FiscalYearId == year &&
                delivery.DocumentNumber == "00079" &&
                delivery.DocumentDate == date && delivery.EffectDate == date &&
                delivery.CurrencyId == currency && delivery.ExchangeRate == 1.2345678m &&
                delivery.Description == "تسليم" && delivery.ExternalReference == "EXT-01" &&
                delivery.Status == "opaque" && delivery.CustomerId == customer &&
                delivery.SalesOrderId == order && delivery.WarehouseId == warehouse &&
                delivery.Recipient == "مستلم" && delivery.ShippingReference == "SHIP-00" &&
                delivery.CreatedBy == f.Owner.Subject && delivery.CreatedAt == at &&
                delivery.LastModifiedAt == at && delivery.Revision == 1,
                "SAL05 header data loss");
            Ensure(delivery.Lines.Count == 2 && delivery.Lines[0].CompanyId == f.CompanyId &&
                delivery.Lines[0].DeliveryId == delivery.Id &&
                delivery.Lines[0].Position == 1 && delivery.Lines[0].ItemId == item &&
                delivery.Lines[0].UnitId == unit &&
                delivery.Lines[0].Quantity == 3.123456m &&
                delivery.Lines[0].LotOrSerial == "0001" &&
                delivery.Lines[1].Position == 2 && delivery.Lines[1].LotOrSerial is null,
                "SAL05 line data loss");
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0,
                "SAL05 draft unexpectedly changed stock or finance");
            return Task.CompletedTask;
        });

        test("SAL05-FG: conditional order, currency, exchange and lot may be absent", f =>
        {
            var delivery = Make(f);
            Ensure(delivery.SalesOrderId is null &&
                delivery.CurrencyId is null && delivery.ExchangeRate is null &&
                delivery.ExternalReference is null && delivery.ShippingReference is null &&
                delivery.Lines[0].LotOrSerial is null, "SAL05 conditional source fields lost");
            return Task.CompletedTask;
        });

        test("SAL05-AC02: bad tenant references, quantities and audit are rejected", f =>
        {
            Reject("sal05_reference_required", () => Make(f, branch: Guid.Empty));
            Reject("sal05_reference_required", () => Make(f, customer: Guid.Empty));
            Reject("sal05_reference_required", () => Make(f, warehouse: Guid.Empty));
            Reject("sal05_reference_required", () => Make(f, salesOrder: Guid.Empty));
            Reject("sal05_reference_required", () => Make(f, currency: Guid.Empty));
            Reject("sal05_dates_required", () => Make(f, date: default(DateOnly)));
            Reject("sal05_audit_date_required", () => Make(f, audit: default(DateTimeOffset)));
            Reject("sal05_exchange_rate_invalid", () => Make(f, exchangeRate: -1m));
            Reject("sal05_exchange_rate_invalid", () => Make(f, exchangeRate: 0m));
            Reject("sal05_line_reference_required", () => Make(f, item: Guid.Empty));
            Reject("invalid_amount", () => Make(f, quantity: 0m));
            Reject("invalid_amount", () => Make(f, quantity: 0.0000001m));
            Reject("invalid_text", () => Make(f, lot: " "));
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0,
                "SAL05 negative contract created side effects");
            return Task.CompletedTask;
        });
    }

    private static SalesDeliveryOrder Make(Fixture f, Guid? branch = null,
        Guid? customer = null, Guid? warehouse = null, Guid? salesOrder = null,
        Guid? currency = null, decimal? exchangeRate = null,
        DateOnly? date = null, DateTimeOffset? audit = null, Guid? item = null,
        decimal quantity = 1m, string? lot = null)
    {
        var docDate = date ?? new DateOnly(2026, 11, 5);
        return new SalesDeliveryOrder(f.CompanyId, branch ?? f.BranchId,
            Guid.NewGuid(), "DO-1", docDate, docDate,
            currency, exchangeRate, "Delivery", null, "opaque",
            customer ?? Guid.NewGuid(), salesOrder,
            warehouse ?? Guid.NewGuid(), "Recipient", null,
            new[] { (item ?? Guid.NewGuid(), Guid.NewGuid(), quantity, lot) },
            f.Owner.Subject, audit ?? new FixedClock().UtcNow);
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("SAL05 expected " + code);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
