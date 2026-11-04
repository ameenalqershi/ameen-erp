using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;

namespace Ameen.Erp.Tests;

public static class PeriodReopenReasonValidationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL12-FG02 partial: reject empty, overlong or control-character reopen reason atomically", async f =>
        {
            var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn.Month == 10);
            var closed = await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id,
                f.Approver, Fixture.Key(), new(period.Version, "GL12-FG02 إقفال"), true, default);
            var auditsBefore = f.Store.Count<AuditEvent>();
            var receiptsBefore = f.Store.Count<IdempotencyRecord>();
            var journalsBefore = f.Store.Count<JournalEntry>();
            var outboxBefore = f.Store.Count<OutboxEvent>();
            foreach (var invalidReason in new[] { "", "   ", new string('r', 501), "bad\nreason" })
            {
                try
                {
                    await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id, f.Approver,
                        Fixture.Key(), new(closed.Version, invalidReason), false, default);
                    throw new Exception("GL12 invalid reason was accepted");
                }
                catch (BusinessException ex) when (ex.Code == "invalid_text") { }
            }
            var stillClosed = f.Store.All<FiscalPeriod>().Single(x => x.Id == period.Id);
            Ensure(stillClosed.IsClosed && stillClosed.Version == closed.Version &&
                   f.Store.Count<AuditEvent>() == auditsBefore &&
                   f.Store.Count<IdempotencyRecord>() == receiptsBefore &&
                   f.Store.Count<JournalEntry>() == journalsBefore &&
                   f.Store.Count<OutboxEvent>() == outboxBefore,
                "Rejected GL12 reopen mutated period, audit, receipts or ledger");
            var reopened = await f.Service.PeriodClose.ChangePeriod(f.CompanyId, period.Id,
                f.Approver, Fixture.Key(), new(closed.Version, "GL12-FG02 سبب معتمد"), false, default);
            var openedPeriod = f.Store.All<FiscalPeriod>().Single(x => x.Id == period.Id);
            Ensure(!openedPeriod.IsClosed && openedPeriod.Version == reopened.Version,
                "Valid GL12 reason did not permit the existing reopen operation");
        });
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
