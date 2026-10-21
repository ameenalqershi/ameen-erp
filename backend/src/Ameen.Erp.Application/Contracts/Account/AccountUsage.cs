using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record AccountUsage(Guid AccountId, int ChildAccountCount, int JournalCount, int JournalLineCount,
    int RecurringTemplateCount, bool HasUsageInSubtree, Page<AccountUsageReference> References);
