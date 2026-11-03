using System.Text.Json;
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
        // SYS14-GAP02: each date bound is optional for the already-deployed audit API.
        var all=await service.ListAudit(cid,owner,0,20,default,
            actorId:"CaseUser",action:"test.audit",resourceId:resource);
        var onlyFrom=await service.ListAudit(cid,owner,0,20,default,
            from:new(2024,2,29),actorId:"CaseUser",action:"test.audit",resourceId:resource);
        var onlyTo=await service.ListAudit(cid,owner,0,20,default,
            to:new(2024,2,29),actorId:"CaseUser",action:"test.audit",resourceId:resource);
        Ensure(all.TotalCount==4 && onlyFrom.TotalCount==3 && onlyTo.TotalCount==3 &&
            onlyFrom.Items.All(x=>x.OccurredAt>=new DateTimeOffset(2024,2,29,0,0,0,TimeSpan.Zero)) &&
            onlyTo.Items.All(x=>x.OccurredAt<new DateTimeOffset(2024,3,1,0,0,0,TimeSpan.Zero)) &&
            (await service.ListAudit(cid,owner,0,20,default,
                new(2024,2,29),new(2024,2,29),"CaseUser","test.audit",resource)).TotalCount==2);
        Console.WriteLine("  SQL PASS SYS14-GAP02 no/one-sided UTC date filters preserve authorized history");
        // SYS14-AC01: edits created by the real company service are recoverable
        // as historical before/after snapshots and link to the same tenant source.
        var companyEdits=await service.ListAudit(cid,owner,0,50,default,
            actorId:owner.Subject,action:"company.edited",resourceId:cid);
        Ensure(companyEdits.TotalCount>0 && companyEdits.Items.All(x=>
            x.CompanyId==cid && x.ActorId==owner.Subject && x.Action=="company.edited" &&
            x.ResourceId==cid && x.OccurredAt!=default));
        var edit=await service.GetAuditEvent(cid,companyEdits.Items[0].Id,owner,default);
        using(var details=JsonDocument.Parse(edit.Details))
            Ensure(details.RootElement.TryGetProperty("before",out var prior) &&
                details.RootElement.TryGetProperty("after",out var newer) &&
                prior.GetProperty("name").GetString()!=null &&
                newer.GetProperty("name").GetString()!=null &&
                !prior.GetRawText().Equals(newer.GetRawText(),StringComparison.Ordinal));
        var company=await db.Set<Company>().AsNoTracking().SingleAsync(x=>x.Id==cid);
        Ensure(company.Id==edit.ResourceId);
        var beforeCount=await db.Set<AuditEvent>().CountAsync(x=>x.CompanyId==cid);
        await service.GetAuditEvent(cid,edit.Id,owner,default);
        await service.ListAudit(cid,owner,0,5,default,action:"company.edited");
        Ensure(await db.Set<AuditEvent>().CountAsync(x=>x.CompanyId==cid)==beforeCount);
        Console.WriteLine("  SQL PASS SYS14-AC01 tracked company-edit actor, time, before/after, source drill and read-only history");
        Console.WriteLine("  SQL PASS SYS14 exact actor/action filters including case and trailing spaces, leap-day boundaries, pagination and event detail");
    }
    private static void Ensure(bool value) { if (!value) throw new Exception("SQL audit filtering assertion failed"); }
}
