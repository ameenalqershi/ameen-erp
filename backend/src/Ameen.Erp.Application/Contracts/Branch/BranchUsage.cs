using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record BranchUsage(Guid BranchId, int ChildCount, int CostCenterCount, int MembershipCount,
    int JournalCount, int RecurringTemplateCount, bool HasSubtreeFinancialUsage);
