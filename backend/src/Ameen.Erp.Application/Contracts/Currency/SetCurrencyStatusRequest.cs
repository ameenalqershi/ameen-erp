using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record SetCurrencyStatusRequest(Guid ExpectedVersion, bool IsActive, string Reason);
