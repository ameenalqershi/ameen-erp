using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// SAL12 is a non-operational entity contract: no credit override is authorized.
public static class CustomerCollectionFollowUpFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SAL12-FG: credit follow-up fields preserve optional promise and request", f =>
        {
            var at = new FixedClock().UtcNow;
            var date = new DateOnly(2026, 11, 5);
            var responsible = Guid.NewGuid();
            var customer = Guid.NewGuid();
            var followup = new CustomerCollectionFollowUp(f.CompanyId,
                f.BranchId, "opaque", customer, date, responsible,
                date.AddDays(7), 100.123456m, "سبب التعديل",
                f.Owner.Subject, at);
            Ensure(followup.CompanyId == f.CompanyId && followup.Id != Guid.Empty &&
                followup.BranchId == f.BranchId && followup.Status == "opaque" &&
                followup.CustomerId == customer && followup.FollowUpDate == date &&
                followup.ResponsibleId == responsible &&
                followup.PromisedPaymentDate == date.AddDays(7) &&
                followup.RequestedCreditOverride == 100.123456m &&
                followup.OverrideReason == "سبب التعديل" &&
                followup.CreatedBy == f.Owner.Subject &&
                followup.CreatedAt == at && followup.LastModifiedAt == at &&
                followup.Revision == 1, "SAL12 follow-up lost original fields");
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0,
                "SAL12 domain construction created financial side effects");
            return Task.CompletedTask;
        });

        test("SAL12-FG: optional branch, promise and override remain absent", f =>
        {
            var followup = Make(f);
            Ensure(followup.BranchId is null &&
                followup.PromisedPaymentDate is null &&
                followup.RequestedCreditOverride is null &&
                followup.OverrideReason is null,
                "SAL12 optional values changed or new approval inferred");
            return Task.CompletedTask;
        });

        test("SAL12-AC02: invalid identity, date, audit, override amount or reason rejected", f =>
        {
            Reject("sal12_reference_required", () => Make(f, customer: Guid.Empty));
            Reject("sal12_reference_required", () => Make(f, branch: Guid.Empty));
            Reject("sal12_reference_required", () => Make(f, responsible: Guid.Empty));
            Reject("sal12_date_required", () => Make(f, followUp: default(DateOnly)));
            Reject("sal12_date_required", () => Make(f, promise: default(DateOnly)));
            Reject("sal12_audit_date_required", () => Make(f, audit: default(DateTimeOffset)));
            Reject("invalid_amount", () => Make(f, request: -1m, reason: "reason"));
            Reject("invalid_amount", () => Make(f, request: 0m, reason: "reason"));
            Reject("invalid_amount", () => Make(f, request: .0000001m, reason: "reason"));
            Reject("sal12_override_reason_required", () =>
                Make(f, request: 20m, reason: " "));
            Reject("invalid_text", () => Make(f, status: " "));
            Ensure(f.Store.Count<JournalEntry>() == 0 &&
                f.Store.Count<OutboxEvent>() == 0,
                "SAL12 denied domain request had a financial effect");
            return Task.CompletedTask;
        });
    }

    private static CustomerCollectionFollowUp Make(Fixture f,
        Guid? branch = null, Guid? customer = null,
        Guid? responsible = null, DateOnly? followUp = null,
        DateOnly? promise = null, DateTimeOffset? audit = null,
        decimal? request = null, string? reason = null, string status = "opaque")
    {
        var date = new DateOnly(2026, 11, 5);
        return new CustomerCollectionFollowUp(f.CompanyId, branch, status,
            customer ?? Guid.NewGuid(), followUp ?? date,
            responsible ?? Guid.NewGuid(), promise, request, reason,
            f.Owner.Subject, audit ?? new FixedClock().UtcNow);
    }

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("SAL12 expected " + code);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
