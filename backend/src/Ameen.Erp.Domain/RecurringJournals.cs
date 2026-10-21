namespace Ameen.Erp.Domain;

public enum RecurringFrequency { Monthly, Quarterly, Yearly }
public enum RecurringGenerationMode { Manual }

public sealed class RecurringJournalTemplate : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = "";
    public RecurringFrequency Frequency { get; private set; }
    public RecurringGenerationMode GenerationMode { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public List<RecurringJournalLine> Lines { get; private set; } = new();
    private RecurringJournalTemplate() { }
    public RecurringJournalTemplate(Guid companyId, Guid branchId, string name, RecurringFrequency frequency,
        RecurringGenerationMode mode, DateOnly startsOn, DateOnly endsOn, string actor, DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "branch_required", "الفرع مطلوب.");
        BranchId = branchId; CreatedBy = Check.Text(actor, "المنشئ"); CreatedAt = now;
        Configure(name, frequency, mode, startsOn, endsOn);
    }
    public void Configure(string name, RecurringFrequency frequency, RecurringGenerationMode mode, DateOnly startsOn, DateOnly endsOn)
    {
        Check.That(Enum.IsDefined(frequency), "invalid_recurring_frequency", "دورية القالب غير صالحة.");
        Check.That(mode == RecurringGenerationMode.Manual, "invalid_generation_mode", "التوليد الحالي يدوي وينتج مسودة فقط.");
        Check.That(endsOn >= startsOn, "invalid_recurring_dates", "نهاية القالب تسبق بدايته.");
        Name = Check.Text(name, "اسم القالب"); Frequency = frequency; GenerationMode = mode;
        StartsOn = startsOn; EndsOn = endsOn; Touch();
    }
    public void SetLines(IEnumerable<RecurringJournalLine> lines)
    {
        var result = lines.ToList();
        Check.That(result.Count is >= 2 and <= 200, "invalid_line_count", "القالب يتكون من 2 إلى 200 سطر.");
        Check.That(result.All(x => x.CompanyId == CompanyId && x.TemplateId == Id), "line_scope_mismatch", "سطور القالب خارج نطاقه.");
        Check.That(result.Select(x => x.Position).Distinct().Count() == result.Count, "duplicate_line_position", "ترتيب السطور مكرر.");
        Lines = result; Touch();
    }
    public void SetActive(bool active) { IsActive = active; Touch(); }

    public DateOnly? DueDateFor(DateOnly periodStartsOn, DateOnly periodEndsOn)
    {
        Check.That(periodEndsOn >= periodStartsOn, "invalid_period", "نهاية الفترة تسبق بدايتها.");
        if (periodEndsOn < StartsOn || periodStartsOn > EndsOn) return null;
        var step = Frequency switch { RecurringFrequency.Monthly => 1, RecurringFrequency.Quarterly => 3, _ => 12 };
        var elapsed = Math.Max(0, (periodStartsOn.Year - StartsOn.Year) * 12 + periodStartsOn.Month - StartsOn.Month);
        var offset = elapsed / step * step;
        var maximumOffset = (9999 - StartsOn.Year) * 12 + 12 - StartsOn.Month;
        DateOnly? Candidate(int months) => months <= maximumOffset ? StartsOn.AddMonths(months) : null;
        var date = Candidate(offset);
        if (date < periodStartsOn) { offset += step; date = Candidate(offset); }
        if (date is null || date > periodEndsOn || date > EndsOn) return null;
        var next = Candidate(offset + step);
        Check.That(next is null || next > periodEndsOn || next > EndsOn,
            "ambiguous_recurring_period", "الفترة تحتوي أكثر من موعد لهذا القالب؛ اختر فترة شهرية محددة.");
        return date;
    }
}

public sealed class RecurringJournalLine : CompanyEntity
{
    public Guid TemplateId { get; private set; }
    public int Position { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string Description { get; private set; } = "";
    public string CurrencyCode { get; private set; } = "";
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    private RecurringJournalLine() { }
    public RecurringJournalLine(Guid companyId, Guid templateId, int position, Guid accountId, Guid? centerId,
        string description, string currency, decimal debit, decimal credit, int currencyScale) : base(companyId)
    {
        Check.Amount(debit, currencyScale); Check.Amount(credit, currencyScale);
        Check.That((debit > 0 && credit == 0) || (credit > 0 && debit == 0), "invalid_journal_side", "السطر يحمل مبلغًا موجبًا في طرف واحد.");
        TemplateId = templateId; Position = position; AccountId = accountId; CostCenterId = centerId;
        Description = Check.Text(description, "بيان السطر", 500); CurrencyCode = Check.Currency(currency);
        Debit = debit; Credit = credit;
    }
}

public sealed class RecurringJournalGeneration : CompanyEntity
{
    public Guid TemplateId { get; private set; }
    public Guid TemplateVersion { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public Guid JournalId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string GeneratedBy { get; private set; } = "";
    public DateTimeOffset GeneratedAt { get; private set; }
    private RecurringJournalGeneration() { }
    public RecurringJournalGeneration(Guid companyId, Guid templateId, Guid templateVersion, Guid periodId,
        Guid journalId, DateOnly dueDate, string actor, DateTimeOffset now) : base(companyId)
    {
        TemplateId = templateId; TemplateVersion = templateVersion; FiscalPeriodId = periodId; JournalId = journalId;
        DueDate = dueDate; GeneratedBy = Check.Text(actor, "المولد"); GeneratedAt = now;
    }
}
