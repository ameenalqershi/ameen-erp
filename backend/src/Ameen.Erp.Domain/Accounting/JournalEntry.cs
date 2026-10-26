namespace Ameen.Erp.Domain;

public sealed class JournalEntry : CompanyEntity
{
    public DateOnly? OperationStartsOn { get; private set; }
    public string? PreviousTrialBalanceReference { get; private set; }
    public string? OpeningCurrencyCode { get; private set; }
    public decimal? OpeningExchangeRate { get; private set; }
    public int? OpeningRevision { get; private set; }
    public DateTimeOffset? OpeningModifiedAt { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string Number { get; private set; } = "";
    public long Sequence { get; private set; }
    public DateOnly DocumentDate { get; private set; }
    public DateOnly AccountingDate { get; private set; }
    public string Description { get; private set; } = "";
    public string? ExternalReference { get; private set; }
    public JournalState State { get; private set; } = JournalState.Draft;
    public JournalSource Source { get; private set; } = JournalSource.Manual;
    public Guid? InventoryOpeningId { get; private set; }
    public Guid? RecurringTemplateId { get; private set; }
    public Guid? SourceTemplateVersion { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public Guid? ReversesJournalId { get; private set; }
    public Guid? ReversalJournalId { get; private set; }
    public Guid? PendingReversalJournalId { get; private set; }
    public string? ReversalReason { get; private set; }
    public List<JournalLine> Lines { get; private set; } = new();
    private JournalEntry() { }
    public JournalEntry(Guid companyId, Guid branchId, Guid periodId, string number, long sequence,
        DateOnly documentDate, DateOnly accountingDate, string description, string? reference,
        string actor, DateTimeOffset now) : base(companyId)
    {
        BranchId = branchId; FiscalPeriodId = periodId; Number = number; Sequence = sequence;
        DocumentDate = documentDate; AccountingDate = accountingDate;
        Description = Check.Text(description, "بيان القيد", 500);
        ExternalReference = reference is null ? null : Check.Text(reference, "المرجع", 100);
        CreatedBy = actor; CreatedAt = now;
    }
    public void SetLines(IEnumerable<JournalLine> lines)
    {
        RequireState(JournalState.Draft);
        var list = lines.ToList();
        Check.That(list.Count is >= 2 and <= 200, "invalid_line_count", "القيد يتكون من 2 إلى 200 سطر.");
        Check.That(list.All(x => x.CompanyId == CompanyId && x.JournalId == Id), "line_scope_mismatch", "سطور القيد خارج نطاق الوثيقة.");
        Check.That(list.Select(x => x.Position).Distinct().Count() == list.Count, "duplicate_line_position", "ترتيب السطور مكرر.");
        Lines = list; Touch();
    }
    public void ValidateBalance()
    {
        Check.That(Lines.Count >= 2 && Lines.Sum(x => x.FunctionalDebit) > 0 &&
            Lines.Sum(x => x.FunctionalDebit) == Lines.Sum(x => x.FunctionalCredit),
            "journal_unbalanced", "القيد غير متزن بالعملة الوظيفية؛ عالج الفرق صراحة.");
    }
    public void Submit()
    {
        RequireState(JournalState.Draft); ValidateBalance(); State = JournalState.Submitted; Touch();
    }
    public void ReturnToDraft()
    {
        RequireState(JournalState.Submitted); State = JournalState.Draft; ApprovedBy = null; Touch();
    }
    public void Approve(string actor, bool separate)
    {
        RequireState(JournalState.Submitted);
        Check.That(!separate || actor != CreatedBy, "self_approval_forbidden",
            "منشئ القيد لا يعتمد القيد نفسه.", ErrorKind.Forbidden);
        ValidateBalance(); ApprovedBy = actor; State = JournalState.Approved; Touch();
    }
    public void Post(DateTimeOffset now)
    {
        RequireState(JournalState.Approved); ValidateBalance(); State = JournalState.Posted;
        PostedAt = now; Touch();
    }
    public void Cancel()
    {
        RequireState(JournalState.Draft); State = JournalState.Cancelled; Touch();
    }
    public void EditDescription(string description)
    {
        RequireState(JournalState.Draft); Description = Check.Text(description, "بيان القيد", 500); Touch();
    }
    public void ReplaceDraftHeader(Guid branchId, Guid periodId, string number, long sequence,
        DateOnly documentDate, DateOnly accountingDate, string? reference, DateOnly? operationStartsOn)
    {
        RequireState(JournalState.Draft);
        Check.That(Source is JournalSource.Manual or JournalSource.GeneralOpening,
            "source_header_immutable", "رأس المستند المولد يعدل من مصدره فقط.", ErrorKind.Conflict);
        Check.That(branchId != Guid.Empty && periodId != Guid.Empty && sequence > 0,
            "invalid_journal_header", "الفرع والفترة وتسلسل القيد مطلوبة.");
        Check.That(documentDate != DateOnly.MinValue && accountingDate != DateOnly.MinValue,
            "journal_dates_required", "تاريخ الوثيقة وتاريخ الأثر مطلوبان.");
        if (Source == JournalSource.GeneralOpening)
            Check.That(operationStartsOn is not null && operationStartsOn != DateOnly.MinValue,
                "opening_start_required", "تاريخ بداية التشغيل مطلوب.");
        else
            Check.That(operationStartsOn is null, "opening_source_required", "تاريخ بداية التشغيل خاص بالافتتاح العام.");
        var checkedNumber = Check.Text(number, "رقم القيد", 64);
        var checkedReference = reference is null ? null : Check.Text(reference, "المرجع", 100);
        BranchId = branchId; FiscalPeriodId = periodId; Number = checkedNumber; Sequence = sequence;
        DocumentDate = documentDate; AccountingDate = accountingDate; ExternalReference = checkedReference;
        OperationStartsOn = operationStartsOn; Touch();
    }
    public void ReserveReversal(Guid reversalId)
    {
        RequireState(JournalState.Posted);
        Check.That(ReversesJournalId is null && PendingReversalJournalId is null && ReversalJournalId is null,
            "already_reversed", "يوجد عكس سابق أو مسودة عكس معلقة للقيد.", ErrorKind.Conflict);
        PendingReversalJournalId = reversalId; Touch();
    }
    public void ReleaseReversal(Guid reversalId)
    {
        Check.That(PendingReversalJournalId == reversalId, "reversal_mismatch", "مرجع العكس غير متوافق.", ErrorKind.Conflict);
        PendingReversalJournalId = null; Touch();
    }
    public void LinkReversal(Guid reversalId)
    {
        RequireState(JournalState.Posted);
        Check.That(ReversesJournalId is null && ReversalJournalId is null && PendingReversalJournalId == reversalId, "already_reversed",
            "لا يمكن عكس قيد عكسي أو قيد له عكس سابق.", ErrorKind.Conflict);
        ReversalJournalId = reversalId; PendingReversalJournalId = null; State = JournalState.Reversed; Touch();
    }
    public void SetReversalSource(Guid originalId, string reason)
    {
        RequireState(JournalState.Draft);
        Check.That(Source == JournalSource.Manual && RecurringTemplateId is null, "journal_source_conflict", "مصدر القيد محدد مسبقًا.", ErrorKind.Conflict);
        ReversesJournalId = originalId; Source = JournalSource.Reversal;
        ReversalReason = Check.Text(reason, "سبب العكس", 500);
    }
    public void SetInventoryOpeningSource(Guid openingId, Guid? originalJournalId = null)
    {
        RequireState(JournalState.Draft);
        Check.That(Source == JournalSource.Manual && openingId != Guid.Empty && ReversesJournalId is null && RecurringTemplateId is null,
            "journal_source_conflict", "مصدر القيد غير صالح.", ErrorKind.Conflict);
        InventoryOpeningId = openingId; ReversesJournalId = originalJournalId; Source = JournalSource.InventoryOpening; Touch();
    }
    public void SetGeneralOpeningSource(DateOnly startsOn, string previousReference, string currency, decimal rate, DateTimeOffset now)
    {
        RequireState(JournalState.Draft);
        Check.That(Source == JournalSource.Manual && ReversesJournalId is null && RecurringTemplateId is null,
            "journal_source_conflict", "مصدر القيد محدد مسبقًا.");
        Check.That(startsOn != DateOnly.MinValue, "opening_start_required", "تاريخ بداية التشغيل مطلوب.");
        Check.That(rate > 0 && rate < 1_000_000_000m && decimal.Round(rate, 10) == rate,
            "invalid_exchange_rate", "سعر صرف الافتتاح يجب أن يكون موجبًا وبدقة معتمدة.");
        Source = JournalSource.GeneralOpening; OperationStartsOn = startsOn;
        PreviousTrialBalanceReference = Check.Text(previousReference, "مرجع الميزان السابق", 500);
        OpeningCurrencyCode = Check.Currency(currency); OpeningExchangeRate = rate; OpeningRevision = 1; OpeningModifiedAt = now; Touch();
    }
    public void EditOpeningReference(string previousReference)
    {
        RequireState(JournalState.Draft);
        Check.That(Source == JournalSource.GeneralOpening, "general_opening_required", "القيد ليس افتتاحًا عامًا.");
        PreviousTrialBalanceReference = Check.Text(previousReference, "مرجع الميزان السابق", 500); Touch();
    }
    public void AdvanceOpening(DateTimeOffset now)
    {
        if (Source != JournalSource.GeneralOpening) return;
        Check.That(OpeningRevision < int.MaxValue, "opening_revision_exhausted", "بلغ إصدار الافتتاح الحد الأقصى.");
        OpeningRevision++; OpeningModifiedAt = now; Touch();
    }
    public void SetRecurringSource(Guid templateId, Guid templateVersion)
    {
        RequireState(JournalState.Draft);
        Check.That(Source == JournalSource.Manual && ReversesJournalId is null && templateId != Guid.Empty && templateVersion != Guid.Empty,
            "journal_source_conflict", "مصدر القيد غير صالح أو محدد مسبقًا.", ErrorKind.Conflict);
        Source = JournalSource.Recurring; RecurringTemplateId = templateId; SourceTemplateVersion = templateVersion; Touch();
    }
    public void RequireState(JournalState state) =>
        Check.That(State == state, "journal_state_conflict", $"حالة القيد يجب أن تكون {state}.", ErrorKind.Conflict);
}
