using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
public sealed record CreateNumberSeriesRequest(NumberingDocumentKind DocumentKind, Guid FiscalYearId, long NextNumber,
    string? Prefix=null, Guid? BranchId=null);
