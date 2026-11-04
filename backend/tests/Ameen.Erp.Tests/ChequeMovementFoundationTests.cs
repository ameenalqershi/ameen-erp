using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

// CASH09 structural acceptance, not verification of state transitions or
// financial settlement. Those rules are gated on source-approved workflows.
public static class ChequeMovementFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("CASH09-01: movement for inbound cheque retains original 13 fields", f =>
        {
            var chequeId = Guid.NewGuid();
            var movement = Create(f, chequeId, null, fee: 0.000001m);
            Ensure(movement.Id != Guid.Empty && movement.CompanyId == f.CompanyId &&
                movement.BranchId == f.BranchId &&
                movement.IncomingChequeId == chequeId &&
                movement.OutgoingChequeId is null && movement.ChequeId == chequeId &&
                movement.State == "Unreviewed" && movement.Action == "Structural" &&
                movement.ActionDate == new DateOnly(2026,11,3) &&
                movement.ReceivedBankAccountId is null &&
                movement.RejectionReason == "000010 reason" &&
                movement.Fee == 0.000001m && movement.CreatedBy == f.Owner.Subject &&
                movement.CreatedAt == new FixedClock().UtcNow &&
                movement.LastModifiedAt == new FixedClock().UtcNow &&
                movement.Revision == 1 && movement.Version != Guid.Empty);
            return Task.CompletedTask;
        });

        test("CASH09-01: movement for outgoing cheque and optional fields are structural only", f =>
        {
            var chequeId = Guid.NewGuid();
            var movement = Create(f, null, chequeId, fee: null, companyScope: true);
            Ensure(movement.ChequeId == chequeId &&
                movement.IncomingChequeId is null &&
                movement.OutgoingChequeId == chequeId &&
                movement.Fee is null && movement.BranchId is null &&
                movement.Revision == 1);
            return Task.CompletedTask;
        });

        test("CASH09-01: no cheque, two cheques, invalid IDs, dates and fees fail", f =>
        {
            Reject("cheque_movement_reference_invalid", () => Create(f, null, null));
            Reject("cheque_movement_reference_invalid", () =>
                Create(f, Guid.NewGuid(), Guid.NewGuid()));
            Reject("cheque_movement_reference_invalid", () => Create(f, Guid.Empty, null));
            Reject("cheque_movement_reference_invalid", () => Create(f, null, Guid.Empty));
            Reject("cheque_movement_scope_invalid", () =>
                Create(f, Guid.NewGuid(), null, branchId: Guid.Empty));
            Reject("cheque_movement_scope_invalid", () =>
                Create(f, Guid.NewGuid(), null, receivedBankId: Guid.Empty));
            Reject("cheque_movement_date_required", () =>
                Create(f, Guid.NewGuid(), null, actionDate: new DateOnly()));
            Reject("cheque_movement_created_at_required", () =>
                Create(f, Guid.NewGuid(), null, createdAt: default(DateTimeOffset)));
            Reject("invalid_amount", () => Create(f, Guid.NewGuid(), null, fee: -1m));
            Reject("invalid_amount", () => Create(f, Guid.NewGuid(), null, fee: 0.0000001m));
            Reject("invalid_text", () => Create(f, Guid.NewGuid(), null, action: "   "));
            return Task.CompletedTask;
        });

        test("CASH09-01: constructing a transition does not collect, cancel or debit", f =>
        {
            _ = Create(f, Guid.NewGuid(), null);
            _ = Create(f, null, Guid.NewGuid(), fee: 1m);
            Ensure(f.Store.All<ChequeMovement>().Count() == 0 &&
                f.Store.All<IncomingCheque>().Count() == 0 &&
                f.Store.All<OutgoingCheque>().Count() == 0 &&
                f.Store.All<JournalEntry>().Count() == 0 &&
                f.Store.All<BankAccount>().Count() == 0);
            return Task.CompletedTask;
        });
    }

    private static ChequeMovement Create(Fixture f, Guid? incoming, Guid? outgoing,
        decimal? fee = null, Guid? branchId = default, Guid? receivedBankId = default,
        DateOnly? actionDate = null, string action = "Structural",
        bool companyScope = false, DateTimeOffset? createdAt = null) =>
        new(f.CompanyId, companyScope ? null : branchId ?? f.BranchId, "Unreviewed",
            incoming, outgoing, action, actionDate ?? new DateOnly(2026,11,3),
            receivedBankId, "000010 reason", fee, f.Owner.Subject,
            createdAt ?? new FixedClock().UtcNow);

    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("CASH09 expected " + code);
    }

    private static void Ensure(bool valid)
    {
        if (!valid) throw new Exception("CASH09 domain assertion failed");
    }
}
