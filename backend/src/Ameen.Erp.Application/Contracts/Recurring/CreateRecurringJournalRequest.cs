using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record CreateRecurringJournalRequest(Guid BranchId, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly EndsOn, RecurringLineRequest[] Lines);
