using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SAL13 has no operational report service yet: filter validation and redaction only.
public static class SalesMovementReportFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SAL13-FG: branch-scoped report preserves all original filter dimensions", f =>
        {
            var other = f.OtherBranchId;
            var customer = Guid.NewGuid();
            var salesperson = Guid.NewGuid();
            var item = Guid.NewGuid();
            var currency = Guid.NewGuid();
            var from = new DateOnly(2026, 11, 1);
            var to = new DateOnly(2026, 11, 30);
            var filter = new SalesMovementReportFilter(f.CompanyId,
                new[] {f.BranchId, other},
                new[] {other, f.BranchId, other}, from, to, currency,
                "mode-opaque", "status-opaque", "detail-opaque",
                customer, salesperson, item, "kind-opaque");
            Ensure(filter.CompanyId == f.CompanyId && filter.BranchIds.Count == 2 &&
                filter.BranchIds[0] == other && filter.BranchIds[1] == f.BranchId &&
                filter.FromDate == from && filter.ToDate == to &&
                filter.DisplayCurrencyId == currency &&
                filter.CurrencyDisplayMode == "mode-opaque" &&
                filter.DocumentStatus == "status-opaque" &&
                filter.DetailLevel == "detail-opaque" &&
                filter.CustomerId == customer && filter.SalespersonId == salesperson &&
                filter.ItemId == item && filter.SaleKind == "kind-opaque",
                "SAL13 report-filter field fidelity lost");
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL13 filter mutated finances");
            return Task.CompletedTask;
        });

        test("SAL13-AC02: external branches and invalid filter contracts are rejected", f =>
        {
            var authorized = new[] {f.BranchId};
            var currency = Guid.NewGuid();
            var date = new DateOnly(2026, 11, 5);
            SalesMovementReportFilter Make(
                Guid company, IEnumerable<Guid> allowed, IEnumerable<Guid> requested,
                DateOnly from, DateOnly to, Guid ccy,
                string mode = "mode", string status = "status", string detail = "detail",
                Guid? customer = null) =>
                new(company, allowed, requested, from, to, ccy,
                    mode, status, detail, customer);
            Reject("sal13_reference_required", () =>
                Make(Guid.Empty, authorized, authorized, date, date, currency));
            Reject("sal13_reference_required", () =>
                Make(f.CompanyId, authorized, authorized, date, date, Guid.Empty));
            Reject("sal13_date_range_invalid", () =>
                Make(f.CompanyId, authorized, authorized, date.AddDays(1), date, currency));
            Reject("sal13_date_range_invalid", () =>
                Make(f.CompanyId, authorized, authorized, default, date, currency));
            Reject("sal13_branch_scope_required", () =>
                Make(f.CompanyId, authorized, Array.Empty<Guid>(), date, date, currency));
            Reject("sal13_branch_scope_required", () =>
                Make(f.CompanyId, Array.Empty<Guid>(), authorized, date, date, currency));
            Reject("sal13_branch_scope_required", () =>
                Make(f.CompanyId, authorized, new[] {Guid.Empty}, date, date, currency));
            Reject("sal13_branch_scope_denied", () =>
                Make(f.CompanyId, authorized, new[] {Guid.NewGuid()}, date, date, currency));
            Reject("sal13_reference_invalid", () =>
                Make(f.CompanyId, authorized, authorized, date, date, currency,
                    customer: Guid.Empty));
            Reject("invalid_text", () =>
                Make(f.CompanyId, authorized, authorized, date, date, currency, mode: " "));
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0, "SAL13 rejected filter created effects");
            return Task.CompletedTask;
        });

        test("SAL13-AC02: cost and margin are hidden without verified permission", f =>
        {
            var source = new SalesMovementReportSourceRow(Guid.NewGuid(),
                120m, 20m, 100m, 70m, 30m);
            var hidden = SalesMovementReportVisibility.ForAuthorizedViewer(source, false);
            var shown = SalesMovementReportVisibility.ForAuthorizedViewer(source, true);
            Ensure(hidden.DocumentId == source.DocumentId &&
                hidden.GrossSales == 120m && hidden.ReturnsAmount == 20m &&
                hidden.NetSales == 100m && hidden.Cost is null &&
                hidden.Profit is null, "SAL13 unprivileged report exposed sensitive fields");
            Ensure(shown.Cost == 70m && shown.Profit == 30m &&
                shown.NetSales == 100m, "SAL13 authorized projection lost fields");
            Reject("sal13_source_required", () =>
                SalesMovementReportVisibility.ForAuthorizedViewer(null!, true));
            return Task.CompletedTask;
        });
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("SAL13 expected " + code);
    }

    private static void Ensure(bool condition, string error)
    {
        if (!condition) throw new Exception(error);
    }
}
