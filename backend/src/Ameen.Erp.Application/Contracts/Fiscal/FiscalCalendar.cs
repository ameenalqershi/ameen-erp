using Ameen.Erp.Domain;

namespace Ameen.Erp.Application;

public sealed record FiscalCalendar(FiscalYear Year, IReadOnlyList<FiscalPeriod> Periods, FiscalCalendarState State, bool GeneralLedgerOnly);
