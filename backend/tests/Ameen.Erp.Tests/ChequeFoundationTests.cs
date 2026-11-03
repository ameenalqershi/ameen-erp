using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH07/CASH08 structural validations only. No receipt, disbursement,
// cancellation, clearing or posting acceptance is inferred from these cases.
public static class ChequeFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH07-01: inbound cheque preserves 22 original fields and zero-prefixed IDs", f =>
        {
            var item = Incoming(f, amount: 450.000001m, rate: 1.1234567890m);
            Ensure(item.CompanyId == f.CompanyId && item.Id != Guid.Empty &&
                item.BranchId == f.BranchId && item.FiscalYearId != Guid.Empty &&
                item.Number == "0000010" && item.ChequeNumber == "000001234" &&
                item.DocumentDate == new DateOnly(2026,11,1) &&
                item.AccountingDate == new DateOnly(2026,11,2) &&
                item.DueDate == new DateOnly(2026,12,1) &&
                item.CurrencyCode == "SAR" && item.ExchangeRate == 1.1234567890m &&
                item.Amount == 450.000001m && item.DrawnBankName == "البنك المسحوب عليه" &&
                item.PayerId != Guid.Empty && item.CollectionAccountId != Guid.Empty &&
                item.State == "Unreviewed" && item.Description == "شيك وارد بنيوي" &&
                item.ExternalReference == "00003" && item.CreatedBy == f.Owner.Subject &&
                item.CreatedAt == new FixedClock().UtcNow &&
                item.LastModifiedAt == new FixedClock().UtcNow &&
                item.Revision == 1 && item.Version != Guid.Empty);
            return Task.CompletedTask;
        });

        test("CASH07-01: invalid inbound cheque IDs, dates, amounts and precision fail", f =>
        {
            Reject("cheque_reference_required", () => Incoming(f, branch: Guid.Empty));
            Reject("cheque_reference_required", () => Incoming(f, payer: Guid.Empty));
            Reject("cheque_reference_required", () => Incoming(f, collection: Guid.Empty));
            Reject("cheque_date_required", () => Incoming(f, dueDate: new DateOnly()));
            Reject("invalid_exchange_rate", () => Incoming(f, rate: 0m));
            Reject("invalid_exchange_rate", () => Incoming(f, rate: 1.00000000001m));
            Reject("invalid_amount", () => Incoming(f, amount: 0m));
            Reject("invalid_amount", () => Incoming(f, amount: 0.0000001m));
            return Task.CompletedTask;
        });

        test("CASH08-01: outbound cheque preserves cheque state and account without posting", f =>
        {
            var item = Outgoing(f, amount: 120.000001m, rate: 1.1234567890m);
            Ensure(item.CompanyId == f.CompanyId && item.Id != Guid.Empty &&
                item.BranchId == f.BranchId && item.FiscalYearId != Guid.Empty &&
                item.Number == "0000020" && item.ChequeNumber == "000000987" &&
                item.CurrencyCode == "SAR" && item.DueDate == new DateOnly(2026,12,1) &&
                item.BankAccountId != Guid.Empty && item.BeneficiaryId != Guid.Empty &&
                item.Amount == 120.000001m && item.ExchangeRate == 1.1234567890m &&
                item.State == "Unreviewed" && item.ChequeState == "Unreviewed" &&
                item.ExternalReference == "00004" && item.Revision == 1 &&
                item.CreatedAt == new FixedClock().UtcNow &&
                item.LastModifiedAt == new FixedClock().UtcNow &&
                f.Store.All<JournalEntry>().Count() == 0);
            return Task.CompletedTask;
        });

        test("CASH08-01: invalid outbound bank, beneficiary, cheque state and money fail", f =>
        {
            Reject("cheque_reference_required", () => Outgoing(f, bank: Guid.Empty));
            Reject("cheque_reference_required", () => Outgoing(f, beneficiary: Guid.Empty));
            Reject("invalid_text", () => Outgoing(f, chequeState: "  "));
            Reject("invalid_exchange_rate", () => Outgoing(f, rate: -1m));
            Reject("invalid_amount", () => Outgoing(f, amount: -1m));
            Reject("invalid_amount", () => Outgoing(f, amount: 0.0000001m));
            Reject("cheque_date_required", () => Outgoing(f, dueDate: new DateOnly()));
            return Task.CompletedTask;
        });

        test("CASH07/CASH08: structural objects never mutate source, cashbox, bank or ledger", f =>
        {
            var before = f.Store.All<JournalEntry>().Count();
            _ = Incoming(f);
            _ = Outgoing(f);
            Ensure(f.Store.All<IncomingCheque>().Count() == 0 &&
                f.Store.All<OutgoingCheque>().Count() == 0 &&
                f.Store.All<CashBox>().Count() == 0 &&
                f.Store.All<BankAccount>().Count() == 0 &&
                f.Store.All<JournalEntry>().Count() == before);
            return Task.CompletedTask;
        });
    }

    private static IncomingCheque Incoming(Fixture f, decimal amount = 1m, decimal rate = 1m,
        Guid? branch = null, Guid? payer = null, Guid? collection = null,
        DateOnly? dueDate = null) =>
        new(f.CompanyId, branch ?? f.BranchId, Guid.NewGuid(), "0000010",
            new(2026,11,1), new(2026,11,2), "SAR", rate,
            "شيك وارد بنيوي", "00003", "Unreviewed", "000001234",
            "البنك المسحوب عليه", payer ?? Guid.NewGuid(), amount,
            dueDate ?? new DateOnly(2026,12,1), collection ?? Guid.NewGuid(),
            f.Owner.Subject, new FixedClock().UtcNow);

    private static OutgoingCheque Outgoing(Fixture f, decimal amount = 1m, decimal rate = 1m,
        Guid? bank = null, Guid? beneficiary = null, DateOnly? dueDate = null,
        string chequeState = "Unreviewed") =>
        new(f.CompanyId, f.BranchId, Guid.NewGuid(), "0000020",
            new(2026,11,1), new(2026,11,2), "SAR", rate,
            "شيك صادر بنيوي", "00004", "Unreviewed", "000000987",
            bank ?? Guid.NewGuid(), beneficiary ?? Guid.NewGuid(), amount,
            dueDate ?? new DateOnly(2026,12,1), chequeState,
            f.Owner.Subject, new FixedClock().UtcNow);

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("CASH cheque test expected " + code);
    }

    private static void Ensure(bool success)
    {
        if (!success) throw new Exception("CASH cheque foundation assertion failed");
    }
}
