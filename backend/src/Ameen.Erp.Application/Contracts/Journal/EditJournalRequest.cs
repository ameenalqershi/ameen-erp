using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditJournalRequest(Guid ExpectedVersion, string Description, JournalLineRequest[] Lines);
