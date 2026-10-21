namespace Ameen.Erp.Domain;

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
