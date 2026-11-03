using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class JournalService
{
    public async Task<JournalPrintDocument> JournalPrint(
        Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        // GetJournal authorizes the exact company, active membership, branch
        // and Read permission before any journal or linked record is displayed.
        var journal = await GetJournal(cid, id, actor, ct);
        Check.That(journal.Source is JournalSource.Manual or JournalSource.Reversal,
            "journal_print_source_unsupported",
            "المستند المولد يطبع من شاشة المصدر، ولا يعرض كقيد يدوي.",
            ErrorKind.Conflict);
        var company = await Company(cid, ct);
        var branch = await Required<Branch>(cid, journal.BranchId, ct);
        string? originalNumber = null;
        if (journal.Source == JournalSource.Reversal)
        {
            Check.That(journal.ReversesJournalId is not null &&
                !string.IsNullOrWhiteSpace(journal.ReversalReason),
                "invalid_reversal_snapshot", "مرجع أصل العكس وسببه مطلوبان.", ErrorKind.Conflict);
            var original = await Required<JournalEntry>(cid, journal.ReversesJournalId!.Value, ct);
            Check.That(original.BranchId == journal.BranchId && original.Id != journal.Id,
                "reversal_scope_mismatch", "العكس خارج نطاق فرع القيد الأصلي.", ErrorKind.Conflict);
            originalNumber = original.Number;
        }
        var accountIds = journal.Lines.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = (await store.Find<Account>(cid, a => accountIds.Contains(a.Id), ct))
            .ToDictionary(x => x.Id);
        Check.That(accounts.Count == accountIds.Length, "account_not_found",
            "لا يمكن طباعة قيد بحسابات مفقودة.", ErrorKind.Conflict);
        var centerIds = journal.Lines.Where(x => x.CostCenterId.HasValue)
            .Select(x => x.CostCenterId!.Value).Distinct().ToArray();
        var centers = (await store.Find<CostCenter>(cid, c => centerIds.Contains(c.Id), ct))
            .ToDictionary(x => x.Id);
        Check.That(centers.Count == centerIds.Length, "cost_center_not_found",
            "لا يمكن طباعة قيد بمراكز تكلفة مفقودة.", ErrorKind.Conflict);
        var rows = journal.Lines.OrderBy(x => x.Position).Select(x =>
        {
            var account = accounts[x.AccountId];
            return new JournalPrintLine(x.Position, account.Code, account.Name,
                x.CostCenterId.HasValue ? centers[x.CostCenterId.Value].Code : null,
                x.Description, x.CurrencyCode, x.ExchangeRate,
                x.Debit, x.Credit, x.FunctionalDebit, x.FunctionalCredit);
        }).ToArray();
        // Summing source amounts across distinct currencies is meaningless.
        // Only functional totals are always aggregated; original totals require one currency.
        var currencies = rows.Select(x => x.CurrencyCode).Distinct().ToArray();
        var singleCurrency = currencies.Length == 1;
        return new JournalPrintDocument(
            journal.Id, journal.Number, company.Name, branch.Name,
            journal.Source.ToString(), journal.State.ToString(), company.FunctionalCurrency,
            journal.DocumentDate, journal.AccountingDate, journal.Description,
            journal.ExternalReference, journal.CreatedBy, journal.CreatedAt,
            journal.LastModifiedAt, journal.Revision, journal.PostedAt,
            originalNumber, journal.ReversalReason, rows,
            singleCurrency ? currencies[0] : null,
            singleCurrency ? rows.Sum(x => x.Debit) : null,
            singleCurrency ? rows.Sum(x => x.Credit) : null,
            rows.Sum(x => x.FunctionalDebit), rows.Sum(x => x.FunctionalCredit));
    }
}
