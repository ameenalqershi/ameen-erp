using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record PeriodCloseIssue(Guid JournalId, Guid BranchId, string Number, JournalState State, string Code);
