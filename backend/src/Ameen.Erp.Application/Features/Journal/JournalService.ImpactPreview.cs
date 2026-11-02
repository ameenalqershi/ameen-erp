using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class JournalService
{
    // GL02-A05: a dedicated entrypoint that rejects other journal sources instead
    // of silently treating a manual or recurring journal as a general opening.
    public async Task<JournalImpactPreview> OpeningImpactPreview(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeJournal(cid, id, actor, Permissions.Read, ct);
        var journal = await Required<JournalEntry>(cid, id, ct);
        Check.That(journal.Source == JournalSource.GeneralOpening, "general_opening_required",
            "المعاينة مخصصة لمستند الافتتاح العام فقط.", ErrorKind.Conflict);
        return await ImpactPreview(cid, id, actor, ct);
    }

    // GL03-A05: show exact persisted functional amounts before submit without booking a new
    // number, audit row, ledger entry or outbox event. Read scope is checked before returning lines.
    public async Task<JournalImpactPreview> ImpactPreview(Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        await AuthorizeJournal(cid, id, actor, Permissions.Read, ct);
        var journal = await Required<JournalEntry>(cid, id, ct);
        Check.That(journal.Source is JournalSource.Manual or JournalSource.Reversal or JournalSource.GeneralOpening,
            "source_preview_required", "معاينة المستند الآلي تتم من مستند المصدر.", ErrorKind.Conflict);
        var company = await Company(cid, ct);
        var debit = journal.Lines.Sum(x => x.FunctionalDebit);
        var credit = journal.Lines.Sum(x => x.FunctionalCredit);
        var difference = debit - credit;
        string? errorCode = null;
        try
        {
            // The same controls as submitting a draft: branch, fiscal period, dimensions,
            // foreign currency snapshots and an exact functional balance.
            await ValidateForPosting(journal, ct);
        }
        catch (BusinessException ex)
        {
            errorCode = ex.Code;
        }
        var lines = journal.Lines.OrderBy(x => x.Position).Select(x => new JournalImpactLine(
            x.Position, x.AccountId, x.CostCenterId, x.CurrencyCode, x.ExchangeRateId,
            x.ExchangeRate, x.Debit, x.Credit, x.FunctionalDebit, x.FunctionalCredit,
            x.FunctionalDebit - x.FunctionalCredit)).ToArray();
        return new(journal.Id, journal.Version, journal.Number, journal.State, journal.Source,
            journal.BranchId, journal.AccountingDate, company.FunctionalCurrency, debit, credit,
            difference, lines.Length >= 2 && debit > 0 && difference == 0,
            journal.State == JournalState.Draft && errorCode is null,
            errorCode, lines);
    }
}
