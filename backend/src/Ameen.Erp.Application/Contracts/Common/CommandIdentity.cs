using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CommandIdentity(Guid CompanyId, string Key, string Operation, string ActorId, string Fingerprint);
