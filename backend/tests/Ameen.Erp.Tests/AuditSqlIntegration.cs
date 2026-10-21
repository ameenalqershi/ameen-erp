using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class AuditSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        var resource = Guid.NewGuid(); var date = new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero);
        var first = new AuditEvent(cid, "CaseUser", "test.audit", resource, date, "{}");
        var last = new AuditEvent(cid, "CaseUser", "test.audit", resource, date.AddDays(1).AddTicks(-1), "{}");
        db.AddRange(first, last,
            new AuditEvent(cid, "caseuser", "test.audit", resource, date, "{}"),
            new AuditEvent(cid, "CaseUser ", "test.audit", resource, date, "{}"),
            new AuditEvent(cid, "CaseUser", "test.AUDIT", resource, date, "{}"),
            new AuditEvent(cid, "CaseUser", "test.audit ", resource, date, "{}"),
            new AuditEvent(cid, "CaseUser", "test.audit", resource, date.AddTicks(-1), "{}"),
            new AuditEvent(cid, "CaseUser", "test.audit", resource, date.AddDays(1), "{}"));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new AuditService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
        var page = await service.ListAudit(cid, owner, 0, 1, default, new(2024, 2, 29), new(2024, 2, 29), "CaseUser", "test.audit", resource);
        Ensure(page.TotalCount == 2 && page.Items.Single().Id == last.Id);
        page = await service.ListAudit(cid, owner, 1, 1, default, new(2024, 2, 29), new(2024, 2, 29), "CaseUser", "test.audit", resource);
        Ensure(page.TotalCount == 2 && page.Items.Single().Id == first.Id);
        Ensure((await service.GetAuditEvent(cid, first.Id, owner, default)).ActorId == "CaseUser");
        Ensure((await service.ListAudit(cid, owner, 0, 10, default, DateOnly.MaxValue, DateOnly.MaxValue, resourceId: resource)).TotalCount == 0);
        Console.WriteLine("  SQL PASS SYS14 exact actor/action filters including case and trailing spaces, leap-day boundaries, pagination and event detail");
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("SQL audit filtering assertion failed"); }
}
