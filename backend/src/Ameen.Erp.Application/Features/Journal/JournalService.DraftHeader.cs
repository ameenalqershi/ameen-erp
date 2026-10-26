using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class JournalService
{
    private async Task AuthorizeDraftEdit(Guid cid, Guid id, Actor actor, DraftJournalHeaderRequest? header, CancellationToken ct)
    {
        await AuthorizeJournal(cid, id, actor, Permissions.Draft, ct);
        if (header is not null) await Authorize(cid, actor, Permissions.Draft, header.BranchId, ct);
    }

    private async Task ReplaceDraftHeader(JournalEntry journal, DraftJournalHeaderRequest? header, CancellationToken ct)
    {
        if (header is null) return;
        journal.RequireState(JournalState.Draft);
        Check.That(journal.Source is JournalSource.Manual or JournalSource.GeneralOpening,
            "source_header_immutable", "رأس المستند المولد يعدل من مصدره فقط.", ErrorKind.Conflict);
        await branchService.ActiveBranch(journal.CompanyId, header.BranchId, ct);
        var previous = await OpenPeriod(journal.CompanyId, journal.AccountingDate, ct);
        var target = await OpenPeriod(journal.CompanyId, header.AccountingDate, ct);
        var number = previous.FiscalYearId == target.FiscalYearId
            ? (journal.Sequence, journal.Number)
            : await NextNumber(journal.CompanyId, target.Id, ct);
        journal.ReplaceDraftHeader(header.BranchId, target.Id, number.Item2, number.Item1,
            header.DocumentDate, header.AccountingDate, header.ExternalReference, header.OperationStartsOn);
    }

    private static object HeaderSnapshot(JournalEntry journal) => new
    {
        journal.BranchId, journal.FiscalPeriodId, journal.Number, journal.Sequence,
        journal.DocumentDate, journal.AccountingDate, journal.ExternalReference, journal.OperationStartsOn
    };
}
