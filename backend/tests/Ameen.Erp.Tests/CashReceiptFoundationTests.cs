using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH03-01 schema-only tests; an unapproved method must never move money.
public static class CashReceiptFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH03-01: 24 original receipt fields preserve leading zeros, dates and money", f =>
        {
            var now = new FixedClock().UtcNow;
            var receipt = Create(f, amount: 120.000001m);
            Ensure(receipt.CompanyId == f.CompanyId && receipt.Id != Guid.Empty &&
                receipt.BranchId == f.BranchId && receipt.Number == "00000017" &&
                receipt.DocumentDate == new DateOnly(2026, 11, 1) &&
                receipt.AccountingDate == new DateOnly(2026, 11, 2) &&
                receipt.CurrencyCode == "SAR" && receipt.ExchangeRate == 1.1234567890m &&
                receipt.Description == "سند قبض بنيوي فقط" &&
                receipt.ExternalReference == "000001" && receipt.State == "Unreviewed" &&
                receipt.PayerId != Guid.Empty && receipt.PaymentMethod == "Unclassified" &&
                receipt.CashBoxId is null && receipt.BankAccountId is null &&
                receipt.Amount == 120.000001m && receipt.ChequeReference == "0000001" &&
                receipt.CreatedBy == f.Owner.Subject &&
                receipt.CreatedAt == now && receipt.LastModifiedAt == now &&
                receipt.Revision == 1 && receipt.Version != Guid.Empty);
            return Task.CompletedTask;
        });

        test("CASH03-01: conditional invoice and discount fields keep decimal precision", f =>
        {
            var parent = Create(f);
            var invoice = Guid.NewGuid();
            var line = new CashReceiptLine(f.CompanyId, parent.Id, invoice,
                20.000001m, 0.000001m);
            var unapplied = new CashReceiptLine(f.CompanyId, parent.Id, null,
                0m, 0m);
            Ensure(line.InvoiceId == invoice && line.ReceiptId == parent.Id &&
                line.AllocatedAmount == 20.000001m &&
                line.SettlementDiscount == 0.000001m &&
                unapplied.InvoiceId is null && unapplied.AllocatedAmount == 0 &&
                unapplied.SettlementDiscount == 0);
            return Task.CompletedTask;
        });

        test("CASH03-01: invalid references, amount and exchange-rate precision fail", f =>
        {
            Reject("cash_receipt_reference_required", () => new CashReceipt(
                f.CompanyId, Guid.Empty, Guid.NewGuid(), "00017", new(2026,11,1),
                new(2026,11,2), "SAR", 1m, "text", null, "Unreviewed",
                Guid.NewGuid(), "Unclassified", null, null, 1m, null,
                f.Owner.Subject, new FixedClock().UtcNow));
            Reject("cash_receipt_date_required", () => new CashReceipt(
                f.CompanyId, f.BranchId, Guid.NewGuid(), "00017", default,
                new(2026,11,2), "SAR", 1m, "text", null, "Unreviewed",
                Guid.NewGuid(), "Unclassified", null, null, 1m, null,
                f.Owner.Subject, new FixedClock().UtcNow));
            Reject("cash_receipt_created_at_required", () => new CashReceipt(
                f.CompanyId, f.BranchId, Guid.NewGuid(), "00017", new(2026,11,1),
                new(2026,11,2), "SAR", 1m, "text", null, "Unreviewed",
                Guid.NewGuid(), "Unclassified", null, null, 1m, null,
                f.Owner.Subject, default));
            Reject("invalid_exchange_rate", () => Create(f, rate: 0m));
            Reject("invalid_exchange_rate", () => Create(f, rate: 1.00000000001m));
            Reject("invalid_amount", () => Create(f, amount: 0m));
            Reject("invalid_amount", () => Create(f, amount: 0.0000001m));
            Reject("cash_receipt_destination_invalid", () => new CashReceipt(
                f.CompanyId, f.BranchId, Guid.NewGuid(), "00017", new(2026,11,1),
                new(2026,11,2), "SAR", 1m, "text", null, "Unreviewed",
                Guid.NewGuid(), "Unclassified", Guid.Empty, null, 1m, null,
                f.Owner.Subject, new FixedClock().UtcNow));
            return Task.CompletedTask;
        });

        test("CASH03-01: invalid invoice line never becomes a posted allocation", f =>
        {
            Reject("cash_receipt_line_reference_invalid", () =>
                new CashReceiptLine(f.CompanyId, Guid.Empty, null, 0m, 0m));
            Reject("cash_receipt_line_reference_invalid", () =>
                new CashReceiptLine(f.CompanyId, Guid.NewGuid(), Guid.Empty, 1m, 0m));
            Reject("invalid_amount", () =>
                new CashReceiptLine(f.CompanyId, Guid.NewGuid(), null, -1m, 0m));
            Reject("invalid_amount", () =>
                new CashReceiptLine(f.CompanyId, Guid.NewGuid(), null, 1m, -1m));
            var receipt = Create(f);
            Ensure(f.Store.All<CashReceipt>().Count() == 0 &&
                f.Store.All<CashReceiptLine>().Count() == 0 &&
                f.Store.All<JournalEntry>().Count() == 0 &&
                receipt.Version != Guid.Empty);
            return Task.CompletedTask;
        });
    }

    private static CashReceipt Create(Fixture f, decimal amount = 1m, decimal rate = 1.1234567890m) =>
        new(f.CompanyId, f.BranchId, Guid.NewGuid(), "00000017",
            new(2026, 11, 1), new(2026, 11, 2), "SAR", rate,
            "سند قبض بنيوي فقط", "000001", "Unreviewed",
            Guid.NewGuid(), "Unclassified", null, null, amount, "0000001",
            f.Owner.Subject, new FixedClock().UtcNow);

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH03 foundation expected " + code);
    }

    private static void Ensure(bool ok)
    {
        if (!ok) throw new Exception("CASH03 foundation test failed");
    }
}
