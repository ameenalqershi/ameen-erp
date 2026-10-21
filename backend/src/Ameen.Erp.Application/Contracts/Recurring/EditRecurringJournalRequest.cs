using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record EditRecurringJournalRequest(Guid ExpectedVersion, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly EndsOn, RecurringLineRequest[] Lines);
