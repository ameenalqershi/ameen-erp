using Ameen.Erp.Domain;
using System.Text.Json.Serialization;

namespace Ameen.Erp.Application;

public sealed record EditJournalRequest(Guid ExpectedVersion, string Description, JournalLineRequest[] Lines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DraftJournalHeaderRequest? Header = null,
    bool? AutomaticReversal = null, DateOnly? ReversalDate = null);
