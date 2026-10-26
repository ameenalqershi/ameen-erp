namespace Ameen.Erp.Domain;

public sealed class RecurringJournalTemplate : CompanyEntity
{
    public Guid BranchId { get; private set; }
    public string Name { get; private set; } = "";
    public RecurringFrequency Frequency { get; private set; }
    public RecurringGenerationMode GenerationMode { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly? EndsOn { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<RecurringJournalLine> Lines { get; private set; } = new();
    private RecurringJournalTemplate() { }
    public RecurringJournalTemplate(Guid companyId, Guid branchId, string name, RecurringFrequency frequency,
        RecurringGenerationMode mode, DateOnly startsOn, DateOnly? endsOn, string actor, DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty, "branch_required", "الفرع مطلوب.");
        BranchId = branchId; CreatedBy = Check.Text(actor, "المنشئ"); CreatedAt = now; LastModifiedAt = now;
        Configure(name, frequency, mode, startsOn, endsOn);
    }
    public void Configure(string name, RecurringFrequency frequency, RecurringGenerationMode mode, DateOnly startsOn, DateOnly? endsOn)
    {
        Check.That(Enum.IsDefined(frequency), "invalid_recurring_frequency", "دورية القالب غير صالحة.");
        Check.That(Enum.IsDefined(mode), "invalid_generation_mode", "طريقة توليد القالب غير صالحة.");
        Check.That(endsOn is null || endsOn >= startsOn, "invalid_recurring_dates", "نهاية القالب تسبق بدايته.");
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

    // Business revision increments once per accepted edit/status operation.
    // Version remains a separate optimistic-concurrency UUID.
    public void AdvanceRevision(DateTimeOffset now)
    {
        Check.That(Revision < int.MaxValue, "recurring_revision_exhausted", "لا يمكن زيادة إصدار القالب بعد الحد الأقصى.");
        Check.That(now >= CreatedAt, "invalid_revision_time", "وقت تعديل القالب يسبق إنشاؤه.");
        Revision++; LastModifiedAt = now; Touch();
    }

    public DateOnly? DueDateFor(DateOnly periodStartsOn, DateOnly periodEndsOn)
    {
        Check.That(periodEndsOn >= periodStartsOn, "invalid_period", "نهاية الفترة تسبق بدايتها.");
        if (periodEndsOn < StartsOn || (EndsOn is DateOnly lastStart && periodStartsOn > lastStart)) return null;
        var step = Frequency switch { RecurringFrequency.Monthly => 1, RecurringFrequency.Quarterly => 3, _ => 12 };
        var elapsed = Math.Max(0, (periodStartsOn.Year - StartsOn.Year) * 12 + periodStartsOn.Month - StartsOn.Month);
        var offset = elapsed / step * step;
        var maximumOffset = (9999 - StartsOn.Year) * 12 + 12 - StartsOn.Month;
        DateOnly? Candidate(int months) => months <= maximumOffset ? StartsOn.AddMonths(months) : null;
        var date = Candidate(offset);
        if (date < periodStartsOn) { offset += step; date = Candidate(offset); }
        if (date is null || date > periodEndsOn || (EndsOn is DateOnly lastDue && date > lastDue)) return null;
        var next = Candidate(offset + step);
        Check.That(next is null || next > periodEndsOn || (EndsOn is DateOnly lastNext && next > lastNext),
            "ambiguous_recurring_period", "الفترة تحتوي أكثر من موعد لهذا القالب؛ اختر فترة شهرية محددة.");
        return date;
    }
}
