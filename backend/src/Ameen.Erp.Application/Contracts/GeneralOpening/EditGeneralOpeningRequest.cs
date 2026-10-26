using System.Text.Json.Serialization;

namespace Ameen.Erp.Application;
public sealed record EditGeneralOpeningRequest(Guid ExpectedVersion, string Description,
    string PreviousTrialBalanceReference, JournalLineRequest[] Lines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DraftJournalHeaderRequest? Header = null);
