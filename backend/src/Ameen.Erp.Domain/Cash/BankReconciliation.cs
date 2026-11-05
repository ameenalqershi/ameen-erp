namespace Ameen.Erp.Domain;

// CASH10-01 is a schema-only snapshot. Bank statement import identity,
// matched ledger movement source, partial/many-to-one allocation, and GL
// adjustments are not approved. EF writes are blocked until that workflow exists.
public sealed class BankReconciliation : CompanyEntity
{
    public Guid? BranchId { get; private set; }
    public string State { get; private set; } = "";
    public Guid BankAccountId { get; private set; }
    public DateOnly StatementFrom { get; private set; }
    public DateOnly StatementTo { get; private set; }
    public decimal OpeningBalance { get; private set; }
    public decimal ClosingBalance { get; private set; }
    public string? StatementFileId { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset LastModifiedAt { get; private set; }

    private BankReconciliation() { }

    public BankReconciliation(Guid companyId, Guid? branchId, string state,
        Guid bankAccountId, DateOnly statementFrom, DateOnly statementTo,
        decimal openingBalance, decimal closingBalance, string? statementFileId,
        string createdBy, DateTimeOffset createdAt) : base(companyId)
    {
        Check.That(branchId != Guid.Empty && bankAccountId != Guid.Empty,
            "bank_reconciliation_reference_invalid",
            "الفرع إن وجد وحساب البنك يجب أن يكونا صالحين.");
        Check.That(statementFrom != default && statementTo != default &&
            statementFrom <= statementTo, "bank_reconciliation_date_invalid",
            "نطاق تاريخ الكشف غير صالح.");
        CheckSignedBalance(openingBalance);
        CheckSignedBalance(closingBalance);
        // CASH10 audit is system-owned even for an unposted structural record.
        // A missing creation instant must not be recorded as year 0001.
        Check.That(createdAt != default, "bank_reconciliation_created_at_required",
            "وقت إنشاء محضر المطابقة مطلوب.");
        BranchId = branchId;
        State = Check.Text(state, "حالة المطابقة", 32);
        BankAccountId = bankAccountId;
        StatementFrom = statementFrom;
        StatementTo = statementTo;
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        StatementFileId = string.IsNullOrWhiteSpace(statementFileId)
            ? null : Check.Text(statementFileId, "معرف ملف الكشف", 300);
        CreatedBy = Check.Text(createdBy, "منشئ المطابقة", 200);
        CreatedAt = createdAt;
        LastModifiedAt = createdAt;
        Revision = 1;
    }

    private static void CheckSignedBalance(decimal value)
    {
        // A bank statement can have a negative closing balance (overdraft).
        Check.That(value > -100_000_000_000_000m &&
            value < 100_000_000_000_000m &&
            decimal.Round(value, 6) == value, "invalid_amount",
            "رصيد الكشف خارج دقة المبلغ.");
    }
}

public sealed class BankReconciliationLine : CompanyEntity
{
    public Guid ReconciliationId { get; private set; }
    public Guid BankMovementId { get; private set; }
    public Guid? LedgerMovementId { get; private set; }
    public decimal MatchedAmount { get; private set; }
    public string? VarianceReason { get; private set; }

    private BankReconciliationLine() { }

    public BankReconciliationLine(Guid companyId, Guid reconciliationId,
        Guid bankMovementId, Guid? ledgerMovementId, decimal matchedAmount,
        string? varianceReason) : base(companyId)
    {
        Check.That(reconciliationId != Guid.Empty && bankMovementId != Guid.Empty &&
            ledgerMovementId != Guid.Empty, "bank_reconciliation_line_reference_invalid",
            "المطابقة وحركة البنك مطلوبتان، ومرجع الدفتر إن وجد صالح.");
        Check.Amount(matchedAmount);
        ReconciliationId = reconciliationId;
        BankMovementId = bankMovementId;
        LedgerMovementId = ledgerMovementId;
        MatchedAmount = matchedAmount;
        VarianceReason = string.IsNullOrWhiteSpace(varianceReason)
            ? null : Check.Text(varianceReason, "سبب الفرق", 1000);
    }
}
