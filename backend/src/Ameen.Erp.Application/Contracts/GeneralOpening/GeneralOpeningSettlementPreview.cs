namespace Ameen.Erp.Application;
public sealed record GeneralOpeningSettlementPreview(Guid JournalId, Guid Version, Guid FiscalYearId,
    string CurrencyCode, DateOnly AccountingDate, bool IncludesUnpostedCandidate, bool AllShownAccountsZero,
    bool AllSubledgersImplemented, OpeningClearingBalance[] Accounts, DateTimeOffset CheckedAt);
