using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CostCenterUsage(Guid CostCenterId, int ChildCenterCount, int JournalCount, int RecurringTemplateCount,
    bool HasUsageInSubtree, Page<AccountUsageReference> References);
