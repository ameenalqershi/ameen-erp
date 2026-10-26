using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class DraftJournalHeaderTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL02: replace draft header and lines together with derived period and stable same-year number", async f =>
        {
            var r = await Opening(f); var before = await f.Journal(r.ResourceId); var oldNumber = before.Number;
            var header = new DraftJournalHeaderRequest(f.OtherBranchId, new(2026, 11, 2), new(2026, 11, 1), "TB-NEW", new(2026, 11, 3));
            r = await Edit(f, r, header, f.Request(125, 125).Lines);
            var after = await f.Journal(r.ResourceId);
            Ensure(after.Number == oldNumber && after.BranchId == f.OtherBranchId && after.DocumentDate == header.DocumentDate && after.AccountingDate == header.AccountingDate);
            Ensure(after.OperationStartsOn == header.OperationStartsOn && after.ExternalReference == "TB-NEW" && after.OpeningRevision == 2 && after.Lines.Sum(x => x.Debit) == 125);
            Ensure(f.Store.All<FiscalPeriod>().Single(x => x.Id == after.FiscalPeriodId).StartsOn == new DateOnly(2026, 11, 1));
            r = await Edit(f, r, header with { ExternalReference = null });
            Ensure((await f.Journal(r.ResourceId)).ExternalReference == null);
            await f.Post(r);
            Ensure((await f.Journal(r.ResourceId)).State == JournalState.Posted);
        });

        test("GL02: cross-year draft edit renumbers once under concurrent replay", async f =>
        {
            await NextYear(f); var r = await Opening(f); var key = Fixture.Key();
            var request = Request(f, r, Header(f, new(2027, 1, 2)));
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, key, request, default)));
            Ensure(results.Distinct().Count() == 1);
            var after = await f.Journal(r.ResourceId);
            Ensure(after.Number == "JV-2027-000001" && after.Sequence == 1 && after.OpeningRevision == 2);
            Ensure(f.Store.All<NumberSequence>().Count() == 2 && f.Store.All<NumberSequence>().All(x => x.LastNumber == 1));
            var next = await f.Draft(f.Request(date: new(2027, 1, 2)));
            Ensure(next.Number == "JV-2027-000002");
            await Error("version_conflict", () => f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), request, default));
        });

        test("GL02: failed cross-year edit rolls back header lines sequence audit and retry key", async f =>
        {
            await NextYear(f); var r = await Opening(f); var audits = f.Store.Count<AuditEvent>(); var key = Fixture.Key();
            var request = Request(f, r, Header(f, new(2027, 1, 2))) with { Lines = f.Request(200, 200).Lines };
            f.Store.FailBeforeCommit = true;
            try { await f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, key, request, default); throw new Exception("Expected persistence failure"); }
            catch (IOException) { }
            var unchanged = await f.Journal(r.ResourceId);
            Ensure(unchanged.Version == r.Version && unchanged.AccountingDate == Fixture.Date && unchanged.OpeningRevision == 1 && unchanged.Lines.Sum(x => x.Debit) == 100);
            Ensure(f.Store.Count<NumberSequence>() == 1 && f.Store.Count<AuditEvent>() == audits);
            await f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, key, request, default);
            Ensure((await f.Journal(r.ResourceId)).Number == "JV-2027-000001");
        });

        test("GL02: old and new periods must be open and target period must exist", async f =>
        {
            var r = await Opening(f);
            await Error("period_not_found", () => Edit(f, r, Header(f, new(2027, 1, 1))));
            var target = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn == new DateOnly(2026, 11, 1)); target.SetClosed(true);
            await Error("period_closed", () => Edit(f, r, Header(f, new(2026, 11, 1))));
            f.Store.All<FiscalPeriod>().Single(x => x.Id == (f.Store.All<JournalEntry>().Single()).FiscalPeriodId).SetClosed(true);
            await Error("period_closed", () => Edit(f, r, Header(f, new(2026, 12, 1))));
            Ensure((await f.Journal(r.ResourceId)).Version == r.Version);
        });

        test("GL02: target branch authorization and cost center scope are revalidated atomically", async f =>
        {
            var r = await Opening(f);
            var denied = Request(f, r, Header(f, Fixture.Date) with { BranchId = f.OtherBranchId });
            await Error("branch_access_denied", () => f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Limited, Fixture.Key(), denied, default));
            var center = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("CC-HEADER", "مركز الفرع", f.BranchId), default);
            var lines = f.Request().Lines; lines[0] = lines[0] with { CostCenterId = center.ResourceId };
            await Error("cost_center_mismatch", () => Edit(f, r, denied.Header!, lines));
            Ensure((await f.Journal(r.ResourceId)).Version == r.Version && (await f.Journal(r.ResourceId)).BranchId == f.BranchId);
        });

        test("GL02: submitted and posted headers stay immutable and opening start is required", async f =>
        {
            var r = await Opening(f);
            await Error("opening_start_required", () => Edit(f, r, Header(f, Fixture.Date) with { OperationStartsOn = null }));
            await Error("journal_dates_required", () => Edit(f, r, Header(f, Fixture.Date) with { DocumentDate = default }));
            r = await f.Action(r, "submit");
            await Error("journal_state_conflict", () => Edit(f, r, Header(f, Fixture.Date)));
            r = await f.Action(r, "approve", f.Approver); r = await f.Action(r, "post");
            await Error("journal_state_conflict", () => Edit(f, r, Header(f, Fixture.Date)));
        });

        test("GL02: legacy edit without header preserves dates and fingerprint shape", async f =>
        {
            var r = await Opening(f);
            var request = new EditGeneralOpeningRequest(r.Version, "تصحيح", "TB-LEGACY", f.Request().Lines);
            Ensure(!JsonSerializer.Serialize(request, JsonDefaults.Options).Contains("header", StringComparison.Ordinal));
            var key = Fixture.Key(); var edited = await f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, key, request, default);
            Ensure(edited == await f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, key, request, default));
            var after = await f.Journal(r.ResourceId);
            Ensure(after.AccountingDate == Fixture.Date && after.OperationStartsOn == Fixture.Date && after.OpeningRevision == 2);
        });

        test("GL03: manual header editing shares period and numbering controls without opening metadata", async f =>
        {
            await NextYear(f); var r = await f.Draft();
            var header = Header(f, new(2027, 2, 1)) with { OperationStartsOn = null, ExternalReference = "MANUAL-NEW" };
            r = await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "يدوي معدل", f.Request().Lines, header), default);
            var after = await f.Journal(r.ResourceId);
            Ensure(after.Source == JournalSource.Manual && after.OpeningRevision == null && after.OperationStartsOn == null && after.Number == "JV-2027-000001");
            await Error("opening_source_required", () => f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "غير صالح", f.Request().Lines, header with { OperationStartsOn = Fixture.Date }), default));
            await f.Post(r);
        });

        test("GL03: changing accounting date requires the new effective FX rate and recomputes functional amounts", async f =>
        {
            await f.Service.Currency.CreateCurrency(f.CompanyId, f.Owner, Fixture.Key(), new("USD", 2, "دولار"), default);
            var oldRate = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", new(2026, 10, 1), 3.75m, "TEST"), default);
            var newRate = await f.Service.ExchangeRate.CreateExchangeRate(f.CompanyId, f.Owner, Fixture.Key(), new("USD", new(2026, 11, 1), 4m, "TEST"), default);
            var request = f.Request(); var lines = request.Lines.Select(x => x with { CurrencyCode = "USD", ExchangeRateId = oldRate.ResourceId }).ToArray();
            var r = await f.Draft(request with { Lines = lines });
            var header = Header(f, new(2026, 11, 2)) with { OperationStartsOn = null };
            await Error("stale_exchange_rate", () => f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "عملة", lines, header), default));
            Ensure((await f.Journal(r.ResourceId)).AccountingDate == Fixture.Date && (await f.Journal(r.ResourceId)).Lines.Sum(x => x.FunctionalDebit) == 375);
            lines = lines.Select(x => x with { ExchangeRateId = newRate.ResourceId }).ToArray();
            await f.Service.Journal.EditJournal(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), new(r.Version, "عملة", lines, header), default);
            Ensure((await f.Journal(r.ResourceId)).Lines.Sum(x => x.FunctionalDebit) == 400);
        });
    }

    internal static DraftJournalHeaderRequest Header(Fixture f, DateOnly date) => new(f.BranchId, date, date, "EDIT-REF", date);
    private static Task<CommandReceipt> NextYear(Fixture f) => f.Service.FiscalCalendar.CreateFiscalYear(f.CompanyId, f.Owner, Fixture.Key(), new("2027", new(2027, 1, 1), new(2027, 12, 31)), default);
    private static Task<CommandReceipt> Opening(Fixture f) => f.Service.Journal.CreateGeneralOpening(f.CompanyId, f.Clerk, Fixture.Key(), GeneralOpeningTests.Request(f.Request()), default);
    private static EditGeneralOpeningRequest Request(Fixture f, CommandReceipt r, DraftJournalHeaderRequest header) => new(r.Version, "تعديل كامل", "TB-EDIT", f.Request().Lines, header);
    private static Task<CommandReceipt> Edit(Fixture f, CommandReceipt r, DraftJournalHeaderRequest header, JournalLineRequest[]? lines = null) =>
        f.Service.Journal.EditGeneralOpening(f.CompanyId, r.ResourceId, f.Clerk, Fixture.Key(), Request(f, r, header) with { Lines = lines ?? f.Request().Lines }, default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Draft header assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
