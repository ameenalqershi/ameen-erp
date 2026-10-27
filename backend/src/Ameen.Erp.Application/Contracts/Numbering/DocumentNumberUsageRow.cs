namespace Ameen.Erp.Application;
public sealed record DocumentNumberUsageRow(Guid AllocationId,long Sequence,string Number,Guid BranchId,string ReservedBy,
    DateTimeOffset ReservedAt,Guid? DocumentId,string? DocumentState);
