using Ameen.Erp.Domain;
using System.Text.Json.Serialization;

namespace Ameen.Erp.Application;

public sealed record EditRecurringJournalRequest(Guid ExpectedVersion, string Name, RecurringFrequency Frequency,
    RecurringGenerationMode GenerationMode, DateOnly StartsOn, DateOnly? EndsOn = null,
    [property: JsonRequired] RecurringLineRequest[] Lines = null!);
