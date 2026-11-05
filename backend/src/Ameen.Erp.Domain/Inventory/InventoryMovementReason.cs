namespace Ameen.Erp.Domain;

// INV16 field foundation only. Direction/status values and posting/approval
// policies are not approved: no service may activate or consume this entity.
public sealed class InventoryMovementReason : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string StatusCode { get; private set; } = "";
    public string ReasonCode { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string DirectionCode { get; private set; } = "";
    public Guid? CounterAccountId { get; private set; }
    public bool RequireCostCenter { get; private set; }
    public Guid ApprovalRouteId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }

    private InventoryMovementReason() { }

    // The status and direction are opaque codes, NOT approved policy values.
    // No create/edit/disable API is exposed. Persistence fails closed.
    public InventoryMovementReason(Guid companyId, Guid? branchId, string statusCode,
        string reasonCode, string description, string directionCode, Guid? counterAccountId,
        bool requireCostCenter, Guid approvalRouteId, string actor, DateTimeOffset now)
        : base(companyId)
    {
        Check.That(branchId != Guid.Empty && counterAccountId != Guid.Empty,
            "invalid_movement_reason_reference", "معرف الفرع أو حساب المقابل غير صالح.");
        Check.That(approvalRouteId != Guid.Empty, "movement_reason_approval_required",
            "مسار اعتماد السبب مطلوب.");
        Check.That(now != default, "movement_reason_audit_time_required",
            "وقت إنشاء السبب مطلوب.");
        var validatedStatus = Check.Code(statusCode, "حالة السبب", 32);
        var validatedCode = Check.Code(reasonCode, "رمز السبب", 32);
        var validatedDescription = Check.Text(description, "وصف السبب", 200);
        var validatedDirection = Check.Code(directionCode, "اتجاه الحركة", 32);
        var validatedActor = Check.Text(actor, "منشئ السبب", 200);
        BranchId = branchId;
        StatusCode = validatedStatus;
        ReasonCode = validatedCode;
        Description = validatedDescription;
        DirectionCode = validatedDirection;
        CounterAccountId = counterAccountId;
        RequireCostCenter = requireCostCenter;
        ApprovalRouteId = approvalRouteId;
        CreatedBy = validatedActor;
        CreatedAt = LastModifiedAt = now;
    }
}
