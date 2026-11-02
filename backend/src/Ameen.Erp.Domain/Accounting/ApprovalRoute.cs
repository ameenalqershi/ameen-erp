namespace Ameen.Erp.Domain;

// SYS09 configuration foundation. No route can be persisted or used for approvals
// until SYS07 role references and SYS09 document-kind/overlap policies are enforced.
public sealed class ApprovalRoute : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string DocumentType { get; private set; } = "";
    public decimal MinimumAmount { get; private set; }
    public decimal? MaximumAmount { get; private set; }
    public Guid CurrencyId { get; private set; }
    public bool SeparateCreatorAndApprover { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<ApprovalRouteStage> Stages { get; private set; } = new();

    private ApprovalRoute() { }

    public ApprovalRoute(Guid companyId, Guid? branchId, string documentType,
        decimal minimumAmount, decimal? maximumAmount, Guid currencyId,
        bool separateCreatorAndApprover, string actor, DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "invalid_approval_branch", "الفرع غير صالح.");
        BranchId = branchId;
        Configure(documentType, minimumAmount, maximumAmount, currencyId, separateCreatorAndApprover);
        CreatedBy = Check.Text(actor, "منشئ مسار الاعتماد", 200);
        CreatedAt = LastModifiedAt = now;
    }

    public void Edit(string documentType, decimal minimumAmount, decimal? maximumAmount,
        Guid currencyId, bool separateCreatorAndApprover, DateTimeOffset now)
    {
        // Validate all inputs before changing the version or state.
        var type = Check.Code(documentType, "نوع المستند", 32);
        ValidateAmounts(minimumAmount, maximumAmount);
        Check.That(currencyId != Guid.Empty, "approval_currency_required", "عملة الحد مطلوبة.");
        Advance(now);
        DocumentType = type;
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        CurrencyId = currencyId;
        SeparateCreatorAndApprover = separateCreatorAndApprover;
    }

    public ApprovalRouteStage AddStage(int number, Guid approverRoleId, int? alertAfterMinutes, DateTimeOffset now)
    {
        Check.That(Stages.All(x => x.StageNumber != number), "duplicate_approval_stage", "رقم المرحلة مكرر.");
        var stage = new ApprovalRouteStage(CompanyId, Id, number, approverRoleId, alertAfterMinutes);
        Advance(now);
        Stages.Add(stage);
        return stage;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        Advance(now);
        IsActive = active;
    }

    private void Configure(string documentType, decimal minimumAmount, decimal? maximumAmount,
        Guid currencyId, bool separateCreatorAndApprover)
    {
        DocumentType = Check.Code(documentType, "نوع المستند", 32);
        ValidateAmounts(minimumAmount, maximumAmount);
        Check.That(currencyId != Guid.Empty, "approval_currency_required", "عملة الحد مطلوبة.");
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        CurrencyId = currencyId;
        SeparateCreatorAndApprover = separateCreatorAndApprover;
    }

    private static void ValidateAmounts(decimal min, decimal? max)
    {
        const decimal limit = 99999999999999.999999m;
        Check.That(min >= 0 && min <= limit && decimal.Round(min, 6) == min &&
            (max is null || max >= min && max <= limit && decimal.Round(max.Value, 6) == max),
            "invalid_approval_amount_range", "حدود المبلغ يجب أن تكون مرتبة وبدقة ست منازل كحد أقصى.");
    }

    private void Advance(DateTimeOffset now)
    {
        Check.That(Revision < int.MaxValue && now >= CreatedAt && now >= LastModifiedAt,
            "invalid_approval_revision", "وقت أو إصدار المسار غير صالح.", ErrorKind.Conflict);
        Revision++;
        LastModifiedAt = now;
        Touch();
    }
}

public sealed class ApprovalRouteStage : CompanyEntity
{
    public Guid ApprovalRouteId { get; private set; }
    public int StageNumber { get; private set; }
    public Guid ApproverRoleId { get; private set; }
    public int? AlertAfterMinutes { get; private set; }

    private ApprovalRouteStage() { }

    internal ApprovalRouteStage(Guid companyId, Guid approvalRouteId, int stageNumber,
        Guid approverRoleId, int? alertAfterMinutes) : base(companyId)
    {
        Check.That(approvalRouteId != Guid.Empty && approverRoleId != Guid.Empty,
            "invalid_approval_role_reference", "مرجع المسار أو دور المعتمد غير صالح.");
        Check.That(stageNumber > 0 && (alertAfterMinutes is null || alertAfterMinutes >= 0),
            "invalid_approval_stage", "رقم المرحلة أو مهلة التنبيه غير صالح.");
        ApprovalRouteId = approvalRouteId;
        StageNumber = stageNumber;
        ApproverRoleId = approverRoleId;
        AlertAfterMinutes = alertAfterMinutes;
    }
}
