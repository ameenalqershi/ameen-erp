using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SAL14 structural quotation validation; not acceptance of quotation lifecycle.
public static class SalesQuotationFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SAL14-FG: quotation fields, prospect, references and two lines retained", f =>
        {
            var at = new FixedClock().UtcNow;
            var year = Guid.NewGuid();
            var currency = Guid.NewGuid();
            var item = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var tax = Guid.NewGuid();
            var customer = Guid.NewGuid();
            var issueDate = new DateOnly(2026, 11, 5);
            var expires = issueDate.AddMonths(1);
            var quote = new SalesQuotation(f.CompanyId, f.BranchId, year, "00018",
                issueDate, issueDate, currency, 1.123456789m, "عرض",
                "EXT-1", "opaque", "عميل محتمل", customer, expires,
                "تسليم", "دفع", "R-02",
                new[] {
                    (item, unit, 3.123456m, 12.123456m, (decimal?)1m, (Guid?)tax),
                    (Guid.NewGuid(), unit, 1m, 0m, (decimal?)null, (Guid?)null)
                }, f.Owner.Subject, at);
            Ensure(quote.CompanyId == f.CompanyId && quote.Id != Guid.Empty &&
                quote.BranchId == f.BranchId && quote.FiscalYearId == year &&
                quote.DocumentNumber == "00018" && quote.DocumentDate == issueDate &&
                quote.EffectDate == issueDate && quote.CurrencyId == currency &&
                quote.ExchangeRate == 1.123456789m && quote.Description == "عرض" &&
                quote.ExternalReference == "EXT-1" && quote.Status == "opaque" &&
                quote.CustomerOrProspect == "عميل محتمل" && quote.CustomerId == customer &&
                quote.ExpiresOn == expires && quote.DeliveryTerms == "تسليم" &&
                quote.PaymentTerms == "دفع" && quote.QuotationIssue == "R-02" &&
                quote.CreatedBy == f.Owner.Subject && quote.CreatedAt == at &&
                quote.LastModifiedAt == at && quote.Revision == 1, "SAL14 header field loss");
            Ensure(quote.Lines.Count == 2 && quote.Lines[0].CompanyId == f.CompanyId &&
                quote.Lines[0].QuotationId == quote.Id && quote.Lines[0].Position == 1 &&
                quote.Lines[0].ItemOrServiceId == item &&
                quote.Lines[0].UnitId == unit &&
                quote.Lines[0].Quantity == 3.123456m &&
                quote.Lines[0].Price == 12.123456m &&
                quote.Lines[0].Discount == 1m &&
                quote.Lines[0].TaxCodeId == tax &&
                quote.Lines[1].Position == 2 && quote.Lines[1].Discount is null &&
                quote.Lines[1].TaxCodeId is null, "SAL14 line fidelity loss");
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<StockMovement>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL14 created unauthorized effects");
            return Task.CompletedTask;
        });

        test("SAL14-FG: prospect-only quotation has no required customer link", f =>
        {
            var quote = Make(f, customer: null);
            Ensure(quote.CustomerId is null && quote.ExternalReference is null &&
                quote.Lines.Count == 1 && quote.Lines[0].TaxCodeId is null &&
                quote.Lines[0].Discount is null, "SAL14 optional fields lost");
            return Task.CompletedTask;
        });

        test("SAL14-AC02: bad refs, dates, audit, rate and line amounts reject", f =>
        {
            Reject("sal14_reference_required", () => Make(f, branch: Guid.Empty));
            Reject("sal14_reference_required", () => Make(f, customer: Guid.Empty));
            Reject("sal14_dates_required", () => Make(f, expires: default(DateOnly)));
            Reject("sal14_audit_date_required", () => Make(f, audit: default(DateTimeOffset)));
            Reject("sal14_exchange_rate_invalid", () => Make(f, rate: 0m));
            Reject("sal14_exchange_rate_invalid", () => Make(f, rate: -1m));
            Reject("sal14_line_reference_required", () => Make(f, lineItem: Guid.Empty));
            Reject("sal14_line_reference_required", () => Make(f, taxCode: Guid.Empty));
            Reject("invalid_amount", () => Make(f, quantity: 0m));
            Reject("invalid_amount", () => Make(f, quantity: 0.0000001m));
            Reject("invalid_amount", () => Make(f, price: -1m));
            Reject("invalid_amount", () => Make(f, discount: -1m));
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL14 rejection had financial effect");
            return Task.CompletedTask;
        });
    }

    private static SalesQuotation Make(Fixture f, Guid? customer = null,
        Guid? branch = null, DateOnly? expires = null, DateTimeOffset? audit = null,
        decimal rate = 1m, Guid? lineItem = null, Guid? taxCode = null,
        decimal quantity = 1m, decimal price = 1m, decimal? discount = null)
    {
        var date = new DateOnly(2026, 11, 5);
        return new SalesQuotation(f.CompanyId, branch ?? f.BranchId, Guid.NewGuid(),
            "Q01", date, date, Guid.NewGuid(), rate, "عرض", null,
            "opaque", "عميل محتمل", customer,
            expires ?? date, "تسليم", "دفع", "v1",
            new[] { (lineItem ?? Guid.NewGuid(), Guid.NewGuid(), quantity, price,
                discount, taxCode) }, f.Owner.Subject, audit ?? new FixedClock().UtcNow);
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("SAL14 expected " + code);
    }

    private static void Ensure(bool condition, string error)
    {
        if (!condition) throw new Exception(error);
    }
}
