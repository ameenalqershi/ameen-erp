using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record LedgerRow(Guid JournalId, string Number, DateOnly AccountingDate, int Position,
    string Description, decimal Debit, decimal Credit, decimal RunningBalance, string CurrencyCode,
    decimal OriginalDebit, decimal OriginalCredit, decimal ExchangeRate);
