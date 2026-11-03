using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class JournalPrintTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL03-A07/GL07-A07: printed manual and reversal preserve historical FX without invalid currency totals", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(),
                new("USD", 2, "دولار"), default);
            var rate = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner,
                Fixture.Key(), new("USD", Fixture.Date, 3.5m, "سعر مثبت"), default);
            var request = f.Request(35m, 35m) with
            {
                Description = "قيد يدوي <img src=x onerror=alert(1)>",
                ExternalReference = "0000070-GL"
            };
            request.Lines[0] = request.Lines[0] with
            {
                CurrencyCode = "USD", ExchangeRateId = rate.ResourceId,
                Debit = 10m, Credit = 0m
            };
            var receipt = await f.Draft(request);
            var audits = f.Store.Count<AuditEvent>();
            var receipts = f.Store.Count<IdempotencyRecord>();
            var numbers = f.Store.All<NumberSequence>().Sum(x => x.LastNumber);
            var manual = await f.Service.Journal.JournalPrint(f.CompanyId, receipt.ResourceId,
                f.Clerk, default);
            Ensure(manual.Source == nameof(JournalSource.Manual) &&
                manual.State == nameof(JournalState.Draft) && manual.Number == receipt.Number &&
                manual.ExternalReference == "0000070-GL" &&
                manual.Lines.Count == 2 && manual.Lines[0].CurrencyCode == "USD" &&
                manual.Lines[0].ExchangeRate == 3.5m && manual.Lines[0].Debit == 10m &&
                manual.Lines[0].FunctionalDebit == 35m &&
                manual.Lines[1].CurrencyCode == "SAR" && manual.Lines[1].ExchangeRate == 1m &&
                manual.Lines[1].Credit == 35m && manual.Lines[1].FunctionalCredit == 35m &&
                manual.OriginalTotalsCurrency is null &&
                manual.TotalDebit is null && manual.TotalCredit is null &&
                manual.FunctionalDebit == 35m && manual.FunctionalCredit == 35m &&
                manual.OriginalJournalNumber is null && manual.ReversalReason is null,
                "Mixed-currency printable journal lost the real line rates or invented source totals");
            Ensure(f.Store.Count<AuditEvent>() == audits &&
                f.Store.Count<IdempotencyRecord>() == receipts &&
                f.Store.All<NumberSequence>().Sum(x => x.LastNumber) == numbers &&
                f.Store.Count<OutboxEvent>() == 0,
                "GL03 print mutated accounting or audit state");

            var original = await f.Post(receipt);
            var reversed = await f.Service.Journal.ReverseJournal(f.CompanyId,
                receipt.ResourceId, f.Clerk, Fixture.Key(),
                new(original.Version, Fixture.Date, "تصحيح تاريخي"), default);
            var before = f.Store.Count<AuditEvent>();
            var reversalPrint = await f.Service.Journal.JournalPrint(f.CompanyId,
                reversed.ResourceId, f.Clerk, default);
            Ensure(reversalPrint.Source == nameof(JournalSource.Reversal) &&
                reversalPrint.State == nameof(JournalState.Draft) &&
                reversalPrint.OriginalJournalNumber == original.Number &&
                reversalPrint.ReversalReason == "تصحيح تاريخي" &&
                reversalPrint.Lines[0].ExchangeRate == 3.5m &&
                reversalPrint.Lines[0].Credit == 10m &&
                reversalPrint.Lines[0].FunctionalCredit == 35m &&
                reversalPrint.Lines[1].CurrencyCode == "SAR" &&
                reversalPrint.Lines[1].Debit == 35m &&
                reversalPrint.Lines[1].FunctionalDebit == 35m &&
                reversalPrint.OriginalTotalsCurrency is null &&
                reversalPrint.TotalDebit is null && reversalPrint.TotalCredit is null &&
                f.Store.Count<AuditEvent>() == before,
                "GL07 reversal print failed to copy opposite historical values and reference");
            var other = await f.Draft(f.Request(branch: f.OtherBranchId));
            await Reject("branch_access_denied", () => f.Service.Journal.JournalPrint(
                f.CompanyId, other.ResourceId, f.Limited, default));
            var opening = await f.Service.Journal.CreateGeneralOpening(f.CompanyId,
                f.Clerk, Fixture.Key(), GeneralOpeningTests.Request(f.Request()), default);
            await Reject("journal_print_source_unsupported", () =>
                f.Service.Journal.JournalPrint(f.CompanyId, opening.ResourceId, f.Clerk, default));
        });
    }

    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("GL03/GL07 print expected " + code);
    }

    private static void Ensure(bool condition, string error)
    {
        if (!condition) throw new Exception(error);
    }
}
