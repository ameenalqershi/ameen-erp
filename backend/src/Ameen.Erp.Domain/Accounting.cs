namespace Ameen.Erp.Domain;

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }
public enum NormalBalance { Debit, Credit }
public enum JournalState { Draft, Submitted, Approved, Posted, Reversed, Cancelled }
public enum JournalSource { Manual, Recurring, Reversal }

public sealed class Account : CompanyEntity
{
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? EnglishName { get; private set; }
    public string? Notes { get; private set; }
    public NormalBalance NormalBalance { get; private set; }
    public int Revision { get; private set; }
    public AccountType Type { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsPosting { get; private set; }
    public bool IsControl { get; private set; }
    public bool RequiresCostCenter { get; private set; }
    public string? CurrencyCode { get; private set; }
    public bool IsActive { get; private set; } = true;
    private Account() { }
    public Account(Guid companyId, string code, string name, AccountType type, Guid? parentId,
        bool posting, bool control, bool requiresCenter, string? currency, NormalBalance normalBalance,
        string? englishName = null, string? notes = null) : base(companyId)
        => Configure(code, name, type, parentId, posting, control, requiresCenter, currency, normalBalance, englishName, notes);

    // Cross-record tree/usage checks belong to the serialized application command.
    public void Configure(string code, string name, AccountType type, Guid? parentId,
        bool posting, bool control, bool requiresCenter, string? currency, NormalBalance normalBalance,
        string? englishName, string? notes)
    {
        Check.That(Enum.IsDefined(typeof(AccountType), type), "invalid_account_type", "تصنيف الحساب غير صالح.");
        Check.That(Enum.IsDefined(normalBalance), "invalid_normal_balance", "طبيعة رصيد الحساب غير صالحة.");
        Code = Check.Code(code, "رقم الحساب"); Name = Check.Text(name, "اسم الحساب");
        EnglishName = englishName is null ? null : Check.Text(englishName, "الاسم الإنجليزي");
        Notes = notes is null ? null : Check.Text(notes, "الملاحظات", 2000);
        NormalBalance = normalBalance;
        Type = type; ParentId = parentId; IsPosting = posting; IsControl = control;
        RequiresCostCenter = requiresCenter;
        CurrencyCode = currency is null ? null : Check.Currency(currency);
        AdvanceRevision();
    }
    public void SetActive(bool active) { IsActive = active; AdvanceRevision(); }
    private void AdvanceRevision()
    {
        Check.That(Revision < int.MaxValue, "account_revision_exhausted", "بلغ إصدار الحساب الحد الأقصى.", ErrorKind.Conflict);
        Revision++; Touch();
    }
}

public sealed class JournalLine : CompanyEntity
{
    public Guid JournalId { get; private set; }
    public int Position { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string Description { get; private set; } = "";
    public string CurrencyCode { get; private set; } = "";
    public Guid? ExchangeRateId { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public decimal FunctionalDebit { get; private set; }
    public decimal FunctionalCredit { get; private set; }
    private JournalLine() { }
    public JournalLine(Guid companyId, Guid journalId, int position, Guid accountId, Guid? centerId,
        string description, string currency, Guid? rateId, decimal rate, decimal debit, decimal credit,
        int currencyScale, int functionalScale) : base(companyId)
    {
        Check.Amount(debit, currencyScale); Check.Amount(credit, currencyScale);
        Check.That((debit > 0 && credit == 0) || (credit > 0 && debit == 0),
            "invalid_journal_side", "السطر يحمل مبلغًا موجبًا في طرف واحد فقط.");
        Check.That(rate > 0 && rate < 1_000_000_000m && decimal.Round(rate, 10) == rate,
            "invalid_exchange_rate", "سعر الصرف غير صالح.");
        var fd = decimal.Round(debit * rate, functionalScale, MidpointRounding.AwayFromZero);
        var fc = decimal.Round(credit * rate, functionalScale, MidpointRounding.AwayFromZero);
        Check.Amount(fd); Check.Amount(fc);
        Check.That(fd + fc > 0, "zero_functional_amount", "قيمة السطر بعد التحويل والتقريب تساوي صفرًا.");
        JournalId = journalId; Position = position; AccountId = accountId; CostCenterId = centerId;
        Description = Check.Text(description, "بيان السطر", 500); CurrencyCode = Check.Currency(currency);
        ExchangeRateId = rateId; ExchangeRate = rate; Debit = debit; Credit = credit;
        FunctionalDebit = fd; FunctionalCredit = fc;
    }
    public JournalLine Opposite(Guid journalId)
    {
        // Reverse the exact historical amounts and snapshots, not today's rates.
        return new JournalLine
        {
            CompanyId = CompanyId, JournalId = journalId, Position = Position,
            AccountId = AccountId, CostCenterId = CostCenterId, Description = Description,
            CurrencyCode = CurrencyCode, ExchangeRateId = ExchangeRateId, ExchangeRate = ExchangeRate,
            Debit = Credit, Credit = Debit, FunctionalDebit = FunctionalCredit, FunctionalCredit = FunctionalDebit
        };
    }
}

public sealed class JournalEntry : CompanyEntity
{
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
