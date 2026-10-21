using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record RecurringLineRequest(Guid AccountId, Guid? CostCenterId, string Description, string CurrencyCode, decimal Debit, decimal Credit);
