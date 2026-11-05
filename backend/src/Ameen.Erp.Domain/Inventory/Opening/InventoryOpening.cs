namespace Ameen.Erp.Domain;
public sealed class InventoryOpening : CompanyEntity
{
    public Guid? ReversesOpeningId { get; private set; }
    public string? ReversalReason { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid FiscalYearId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string Number { get; private set; } = "";
    public DateOnly DocumentDate { get; private set; }
    public DateOnly AccountingDate { get; private set; }
    public DateOnly CutoffDate { get; private set; }
    public string CurrencyCode { get; private set; } = "";
    public decimal ExchangeRate { get; private set; } = 1;
    public string CountReference { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string? ExternalReference { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ModifiedAt { get; private set; }
    public int Revision { get; private set; }
    public InventoryOpeningState State { get; private set; }
    public Guid? JournalId { get; private set; }
    public Guid? InventoryAccountId { get; private set; }
    public Guid? ClearingAccountId { get; private set; }
    public Guid? PostingSettingsVersion { get; private set; }
    public string? PostedBy { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public List<InventoryOpeningLine> Lines { get; private set; } = new();
    private InventoryOpening() { }
    public InventoryOpening(Guid cid, Guid branch, Guid warehouse, FiscalPeriod period, string number,
        DateOnly documentDate, DateOnly accountingDate, DateOnly cutoffDate, string currency, string countReference,
        string description, string? externalReference, string actor, DateTimeOffset now) : base(cid)
    {
        Check.That(documentDate != DateOnly.MinValue && accountingDate != DateOnly.MinValue && cutoffDate != DateOnly.MinValue,
            "opening_dates_required", "تواريخ المستند والأثر والقطع مطلوبة.");
        Check.That(now != default, "opening_audit_time_required", "وقت إنشاء الافتتاح مطلوب.");
        var createdBy = Check.Text(actor, "منشئ الافتتاح", 200);
        BranchId = branch; WarehouseId = warehouse; FiscalPeriodId = period.Id; FiscalYearId = period.FiscalYearId; Number = number;
        DocumentDate = documentDate; AccountingDate = accountingDate; CutoffDate = cutoffDate; CurrencyCode = Check.Currency(currency);
        CountReference = Check.Text(countReference, "مرجع الجرد", 100); Description = Check.Text(description, "البيان", 500);
        ExternalReference = externalReference is null ? null : Check.Text(externalReference, "المرجع", 100);
        CreatedBy = createdBy; CreatedAt = now; ModifiedAt = now;
    }
    public void Edit(string description, IEnumerable<InventoryOpeningLine> lines, DateTimeOffset now)
    {
        Check.That(State is InventoryOpeningState.Draft or InventoryOpeningState.Returned, "opening_state_conflict", "التعديل لمسودة أو مستند معاد فقط.", ErrorKind.Conflict);
        PreflightAdvance(now);
        var list = lines.ToList();
        Check.That(list.Count is >= 1 and <= 200, "invalid_opening_lines", "المستند يتكون من 1 إلى 200 سطر.");
        Check.That(list.All(x => x.CompanyId == CompanyId && x.OpeningId == Id), "opening_line_scope", "السطر خارج المستند.");
        Check.That(list.Select(x => x.Position).Distinct().Count() == list.Count, "duplicate_line_position", "ترتيب مكرر.");
        Check.That(list.Sum(x => x.Value) < 100_000_000_000_000m, "opening_value_overflow", "قيمة المستند تتجاوز الحد.");
        Description = Check.Text(description, "البيان", 500); Lines = list; State = InventoryOpeningState.Draft; Advance(now);
    }
    public void Transition(string action, string actor, DateTimeOffset now)
    {
        var required = action switch { "submit" or "cancel" => InventoryOpeningState.Draft, "approve" or "return" => InventoryOpeningState.Submitted, _ => throw new BusinessException("invalid_transition", "الإجراء غير متاح.") };
        Check.That(State == required, "opening_state_conflict", "حالة المستند لا تسمح بالإجراء.", ErrorKind.Conflict);
        PreflightAdvance(now);
        var performedBy = Check.Text(actor, "منفذ إجراء الافتتاح", 200);
        if (action == "approve")
        {
            Check.That(performedBy != CreatedBy, "self_approval_forbidden", "منشئ المستند لا يعتمده.", ErrorKind.Forbidden);
            ApprovedBy = performedBy;
        }
        State = action switch { "submit" => InventoryOpeningState.Submitted, "approve" => InventoryOpeningState.Approved, "return" => InventoryOpeningState.Returned, _ => InventoryOpeningState.Cancelled };
        Advance(now);
    }
    public void SetReversalSource(Guid sourceId, string reason)
    {
        Check.That(State == InventoryOpeningState.Draft && ReversesOpeningId is null && sourceId != Guid.Empty && sourceId != Id,
            "opening_reversal_source_conflict", "مصدر العكس غير صالح.", ErrorKind.Conflict);
        var validatedReason = Check.Text(reason, "سبب العكس", 500);
        ReversesOpeningId = sourceId; ReversalReason = validatedReason; Touch();
    }
    public void CorrectReversal(string reason, DateTimeOffset now)
    {
        Check.That(ReversesOpeningId is not null && State == InventoryOpeningState.Returned,
            "opening_state_conflict", "التصحيح لمستند عكس معاد فقط.", ErrorKind.Conflict);
        PreflightAdvance(now);
        ReversalReason = Check.Text(reason, "سبب العكس", 500); State = InventoryOpeningState.Draft; Advance(now);
    }
    public void Post(Guid journalId, Guid inventoryAccountId, Guid clearingAccountId, Guid settingsVersion, string actor, DateTimeOffset now)
    {
        Check.That(State == InventoryOpeningState.Approved && ApprovedBy is not null, "opening_state_conflict", "الترحيل يتطلب مستندًا معتمدًا.", ErrorKind.Conflict);
        Check.That(journalId != Guid.Empty && inventoryAccountId != Guid.Empty && clearingAccountId != Guid.Empty && settingsVersion != Guid.Empty,
            "invalid_stock_reference", "مراجع الترحيل مطلوبة.");
        PreflightAdvance(now);
        var postedBy = Check.Text(actor, "مرحل الافتتاح", 200);
        JournalId = journalId; InventoryAccountId = inventoryAccountId; ClearingAccountId = clearingAccountId;
        PostingSettingsVersion = settingsVersion; PostedBy = postedBy; PostedAt = now; State = InventoryOpeningState.Posted; Advance(now);
    }
    private void PreflightAdvance(DateTimeOffset now)
    {
        Check.That(now != default, "opening_audit_time_required", "وقت تغيير الافتتاح مطلوب.");
        Check.That(Revision < int.MaxValue, "opening_revision_exhausted", "بلغ الإصدار الحد الأقصى.", ErrorKind.Conflict);
    }
    private void Advance(DateTimeOffset now)
    {
        PreflightAdvance(now);
        Revision++; ModifiedAt = now; Touch();
    }
}
