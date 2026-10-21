using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record JournalLineRequest(Guid AccountId, Guid? CostCenterId, string Description,
    string CurrencyCode, Guid? ExchangeRateId, decimal Debit, decimal Credit);
