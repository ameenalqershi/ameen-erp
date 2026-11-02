using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record JournalSummary(Guid Id, Guid Version, string Number, Guid BranchId, DateOnly AccountingDate,
    string Description, JournalState State, decimal Debit, decimal Credit, JournalSource Source, Guid? RecurringTemplateId,
    int Revision, DateTimeOffset LastModifiedAt, bool AutomaticReversal = false, DateOnly? AutomaticReversalDate = null);
