namespace Ameen.Erp.Domain;

// CASH11 structural foundation only. No live drawer balance, reconciliation,
// approval state machine, financial adjustment, or posting is enabled.
public sealed class CashCount : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string State { get; private set; } = "";
    public Guid CashBoxId { get; private set; }
    public DateTimeOffset CutoffAt { get; private set; }
    public Guid CountedByMembershipId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private CashCount() { }

    // The lifecycle values are intentionally NOT defined by this structural
    // model: an approved CASH11 workflow must decide the allowed state list.
    public CashCount(Guid companyId, Guid? branchId, string state, Guid cashBoxId,
        DateTimeOffset cutoffAt, Guid countedByMembershipId, string createdBy,
        DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "cash_count_branch_invalid",
            "مرجع الفرع إن وجد يجب أن يكون صحيحًا.");
        Check.That(cashBoxId != Guid.Empty && countedByMembershipId != Guid.Empty,
            "cash_count_reference_required", "الصندوق والقائم بالجرد مطلوبان.");
        Check.That(cutoffAt != default, "cash_count_cutoff_required",
            "وقت قطع جرد الصندوق مطلوب.");
        BranchId = branchId;
        State = Check.Text(state, "حالة محضر الجرد", 32);
        CashBoxId = cashBoxId;
        CutoffAt = cutoffAt;
        CountedByMembershipId = countedByMembershipId;
        CreatedBy = Check.Text(createdBy, "منشئ الجرد");
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }
}

// A line's amount is server-derived from its decimal denomination and integer
// piece count; SQL persists a computed column and never accepts direct writes.
public sealed class CashCountLine : CompanyEntity
{
    public Guid CashCountId { get; private set; }
    public decimal Denomination { get; private set; }
    public int PieceCount { get; private set; }
    public decimal CountedValue { get; private set; }

    private CashCountLine() { }

    public CashCountLine(Guid companyId, Guid cashCountId, decimal denomination,
        int pieceCount) : base(companyId)
    {
        Check.That(cashCountId != Guid.Empty, "cash_count_reference_required",
            "محضر الجرد مطلوب.");
        Check.Amount(denomination, positive: true);
        Check.That(pieceCount >= 0, "cash_count_piece_count_invalid",
            "عدد القطع لا يكون سالبًا.");
        decimal counted;
        try { counted = checked(denomination * pieceCount); }
        catch (OverflowException)
        {
            throw new BusinessException("invalid_amount", "القيمة المعدودة خارج النطاق.");
        }
        Check.Amount(counted);
        CashCountId = cashCountId;
        Denomination = denomination;
        PieceCount = pieceCount;
        CountedValue = counted;
    }
}
