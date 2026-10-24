namespace Ameen.Erp.Application;
public sealed record EditGeneralOpeningRequest(Guid ExpectedVersion, string Description,
    string PreviousTrialBalanceReference, JournalLineRequest[] Lines);
