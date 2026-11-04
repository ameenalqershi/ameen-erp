namespace Ameen.Erp.Domain;

// CASH09 structural-only transition record. The approved state transition
// graph and financial impact are not available. All EF writes remain blocked.
public sealed class ChequeMovement : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string State { get; private set; } = "";
    // Physical resolution of the single original CASH09 cheque reference.
    // Exactly one is required; both have a company-scoped SQL FK.
    public Guid? IncomingChequeId { get; private set; }
    public Guid? OutgoingChequeId { get; private set; }
    public Guid ChequeId => IncomingChequeId ?? OutgoingChequeId ?? Guid.Empty;
    public string Action { get; private set; } = "";
    public DateOnly ActionDate { get; private set; }
    public Guid? ReceivedBankAccountId { get; private set; }
    public string? RejectionReason { get; private set; }
    public decimal? Fee { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private ChequeMovement() { }

    public ChequeMovement(Guid companyId, Guid? branchId, string state,
        Guid? incomingChequeId, Guid? outgoingChequeId, string action,
        DateOnly actionDate, Guid? receivedBankAccountId,
        string? rejectionReason, decimal? fee, string createdBy,
        DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && receivedBankAccountId != Guid.Empty,
            "cheque_movement_scope_invalid", "مرجع الفرع أو البنك المستلم غير صحيح.");
        Check.That(incomingChequeId.HasValue != outgoingChequeId.HasValue &&
            incomingChequeId != Guid.Empty && outgoingChequeId != Guid.Empty,
            "cheque_movement_reference_invalid",
            "يجب ربط المحضر بشيك وارد أو صادر واحد ضمن الشركة.");
        Check.That(actionDate != default, "cheque_movement_date_required",
            "تاريخ إجراء الشيك مطلوب.");
        Check.That(createdAt != default, "cheque_movement_created_at_required",
            "وقت إنشاء حركة الشيك مطلوب.");
        if (fee.HasValue)
            Check.Amount(fee.Value);
        BranchId = branchId;
        State = Check.Text(state, "حالة المحضر", 32);
        IncomingChequeId = incomingChequeId;
        OutgoingChequeId = outgoingChequeId;
        Action = Check.Text(action, "الإجراء", 32);
        ActionDate = actionDate;
        ReceivedBankAccountId = receivedBankAccountId;
        RejectionReason = string.IsNullOrWhiteSpace(rejectionReason) ? null :
            Check.Text(rejectionReason, "سبب الرفض أو الإلغاء", 1000);
        Fee = fee;
        CreatedBy = Check.Text(createdBy, "منشئ الحركة", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
