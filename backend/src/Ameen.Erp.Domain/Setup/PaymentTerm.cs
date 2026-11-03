namespace Ameen.Erp.Domain;

// Values are the three named means in SYS16's original purpose. No payment,
// card-clearing or POS posting path is activated by this schema-only card.
public enum PaymentMethodKind { Cash, Card, Credit }

public sealed class PaymentTerm : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public PaymentMethodKind Kind { get; private set; }
    public Guid? SettlementAccountId { get; private set; }
    public int GraceDays { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; } = 1;
    public DateTimeOffset LastModifiedAt { get; private set; }
    public List<PaymentTermLine> Lines { get; private set; } = new();

    private PaymentTerm() { }

    public PaymentTerm(Guid companyId, Guid? branchId, string code, string name,
        PaymentMethodKind kind, Guid? settlementAccountId, int graceDays,
        IReadOnlyList<(decimal Percent, int DaysFromBase)>? schedule,
        string createdBy, DateTimeOffset now) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && settlementAccountId != Guid.Empty,
            "payment_term_reference_invalid", "مرجع الفرع أو حساب التسوية غير صالح.");
        Check.That(Enum.IsDefined(kind), "payment_method_kind_invalid",
            "نوع وسيلة الدفع غير صالح.");
        Check.That(graceDays >= 0, "payment_grace_days_invalid",
            "أيام السماح لا تكون سالبة.");
        var validatedCode = Check.Code(code, "رمز طريقة الدفع");
        var validatedName = Check.Text(name, "اسم طريقة الدفع");
        var validatedActor = Check.Text(createdBy, "منشئ طريقة الدفع", 200);
        var plan = schedule?.ToArray() ?? Array.Empty<(decimal Percent, int DaysFromBase)>();
        foreach (var (percent, days) in plan)
        {
            Check.That(percent > 0m && percent <= 100m &&
                decimal.Round(percent, 6) == percent,
                "payment_percentage_invalid", "نسبة الدفعة موجبة وبحد أقصى ست منازل عشرية.");
            Check.That(days >= 0, "payment_days_invalid",
                "تاريخ استحقاق الدفعة لا يسبق تاريخ الأساس.");
        }
        // Only the explicit SYS16 rule is enforced: when a plan is provided,
        // its percentages must total exactly 100. Nothing creates a cash receipt.
        Check.That(plan.Length == 0 || plan.Sum(x => x.Percent) == 100m,
            "payment_plan_total_invalid", "مجموع نسب خطة الأقساط يجب أن يساوي 100%.");
        BranchId = branchId; Code = validatedCode; Name = validatedName;
        Kind = kind; SettlementAccountId = settlementAccountId; GraceDays = graceDays;
        CreatedBy = validatedActor; CreatedAt = now; LastModifiedAt = now;
        Lines = plan.Select((x, i) =>
            new PaymentTermLine(companyId, Id, i + 1, x.Percent, x.DaysFromBase)).ToList();
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        Check.That(now >= CreatedAt && now >= LastModifiedAt,
            "payment_term_audit_time_invalid", "وقت التعديل لا يسبق وقت الإنشاء.");
        Check.That(Revision < int.MaxValue, "payment_term_revision_exhausted",
            "بلغ رقم إصدار طريقة الدفع الحد الأقصى.", ErrorKind.Conflict);
        IsActive = active; Revision++; LastModifiedAt = now; Touch();
    }
}

public sealed class PaymentTermLine : CompanyEntity
{
    public Guid TermId { get; private set; }
    public int Position { get; private set; }
    public decimal Percent { get; private set; }
    public int DaysFromBase { get; private set; }

    private PaymentTermLine() { }

    public PaymentTermLine(Guid companyId, Guid termId, int position,
        decimal percent, int daysFromBase) : base(companyId)
    {
        Check.That(termId != Guid.Empty && position > 0,
            "payment_term_line_invalid", "مرجع أو ترتيب الدفعة غير صالح.");
        Check.That(percent > 0m && percent <= 100m &&
            decimal.Round(percent, 6) == percent,
            "payment_percentage_invalid", "نسبة الدفعة غير صالحة.");
        Check.That(daysFromBase >= 0, "payment_days_invalid",
            "أيام الاستحقاق غير صالحة.");
        TermId = termId; Position = position;
        Percent = percent; DaysFromBase = daysFromBase;
    }
}
