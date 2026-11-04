namespace Ameen.Erp.Domain;

// SAL12 structural collection/credit follow-up only. No credit exception is
// approved here: policy for exposure, duration, amount and approver is pending.
public sealed class CustomerCollectionFollowUp : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string Status { get; private set; } = "";
    public Guid CustomerId { get; private set; }
    public DateOnly FollowUpDate { get; private set; }
    public Guid ResponsibleId { get; private set; }
    public DateOnly? PromisedPaymentDate { get; private set; }
    public decimal? RequestedCreditOverride { get; private set; }
    public string? OverrideReason { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CustomerCollectionFollowUp() { }

    public CustomerCollectionFollowUp(Guid companyId, Guid? branchId,
        string status, Guid customerId, DateOnly followUpDate,
        Guid responsibleId, DateOnly? promisedPaymentDate,
        decimal? requestedCreditOverride, string? overrideReason,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && customerId != Guid.Empty &&
            responsibleId != Guid.Empty, "sal12_reference_required",
            "العميل والمسؤول مراجع إلزامية والفرع الاختياري يجب أن يكون صالحا.");
        Check.That(followUpDate != default &&
            promisedPaymentDate != default(DateOnly),
            "sal12_date_required", "تاريخ المتابعة أو الوعد غير صالح.");
        Check.That(createdAt != default, "sal12_audit_date_required",
            "وقت الإنشاء مطلوب.");
        if (requestedCreditOverride.HasValue)
        {
            Check.Amount(requestedCreditOverride.Value, positive: true);
            Check.That(!string.IsNullOrWhiteSpace(overrideReason),
                "sal12_override_reason_required", "سبب طلب تجاوز الائتمان مطلوب.");
        }

        var validatedStatus = Check.Text(status, "حالة المتابعة", 64);
        var validatedReason = overrideReason is null ? null :
            Check.Text(overrideReason, "سبب التجاوز", 2000);
        var validatedActor = Check.Text(createdBy, "منشئ متابعة التحصيل", 200);

        BranchId = branchId;
        Status = validatedStatus;
        CustomerId = customerId;
        FollowUpDate = followUpDate;
        ResponsibleId = responsibleId;
        PromisedPaymentDate = promisedPaymentDate;
        RequestedCreditOverride = requestedCreditOverride;
        OverrideReason = validatedReason;
        CreatedBy = validatedActor;
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}
