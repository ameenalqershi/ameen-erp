using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

// GL11-A05: current state plus immutable period-close/reopen history.
// The global audit/period-close/read permissions are enforced before the data
// is materialized and the response never creates another accounting action.
public sealed record PeriodExecutionSummary(
    Guid PeriodId, string PeriodCode, bool IsClosed, Guid Version,
    Page<AuditEvent> History);
