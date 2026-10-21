using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record RecurringJournalSummary(Guid Id, Guid Version, Guid BranchId, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly EndsOn, bool IsActive, int LineCount);
