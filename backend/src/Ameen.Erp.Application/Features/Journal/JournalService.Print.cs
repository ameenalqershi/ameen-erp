using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed partial class JournalService
{
    // GL02-A07: the snapshot is obtained after GetGeneralOpening's tenant,
    // branch and Read authorization. It never allocates numbers or writes audit.
    public async Task<GeneralOpeningPrintDocument> GeneralOpeningPrint(
        Guid cid, Guid id, Actor actor, CancellationToken ct)
    {
        var opening = await GetGeneralOpening(cid, id, actor, ct);
        var company = await Company(cid, ct);
        var branch = await Required<Branch>(cid, opening.BranchId, ct);
        var accountIds = opening.Lines.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = (await store.Find<Account>(cid, a => accountIds.Contains(a.Id), ct))
            .ToDictionary(a => a.Id);
        Check.That(accounts.Count == accountIds.Length, "account_not_found",
            "لا يمكن طباعة افتتاح بحسابات مفقودة.", ErrorKind.Conflict);
        var centerIds = opening.Lines.Where(x => x.CostCenterId.HasValue)
            .Select(x => x.CostCenterId!.Value).Distinct().ToArray();
        var centers = (await store.Find<CostCenter>(cid, c => centerIds.Contains(c.Id), ct))
            .ToDictionary(c => c.Id);
        Check.That(centers.Count == centerIds.Length, "cost_center_not_found",
            "لا يمكن طباعة افتتاح بمراكز تكلفة مفقودة.", ErrorKind.Conflict);

        var rows = opening.Lines.OrderBy(x => x.Position)
            .Select(x =>
            {
                var account = accounts[x.AccountId];
                return new JournalPrintLine(
                    x.Position, account.Code, account.Name,
                    x.CostCenterId.HasValue ? centers[x.CostCenterId.Value].Code : null,
                    x.Description, x.CurrencyCode, x.ExchangeRate,
                    x.Debit, x.Credit, x.FunctionalDebit, x.FunctionalCredit);
            }).ToArray();
        return new GeneralOpeningPrintDocument(
            opening.Id, opening.Number, company.Name, branch.Name,
            opening.State.ToString(), company.FunctionalCurrency,
            opening.OpeningCurrencyCode!, opening.OpeningExchangeRate!.Value,
            opening.DocumentDate, opening.AccountingDate, opening.OperationStartsOn!.Value,
            opening.PreviousTrialBalanceReference!, opening.Description,
            opening.ExternalReference, opening.CreatedBy, opening.CreatedAt,
            opening.OpeningRevision ?? opening.Revision, rows,
            rows.Sum(x => x.Debit), rows.Sum(x => x.Credit),
            rows.Sum(x => x.FunctionalDebit), rows.Sum(x => x.FunctionalCredit));
    }
}
