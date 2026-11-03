using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
namespace Ameen.Erp.Tests;
public static class AuditTests
{
    private static AuditEvent Add(Fixture f, string actor, string action, DateTimeOffset at, Guid resource, Guid? company = null)
    { var e = new AuditEvent(company ?? f.CompanyId, actor, action, resource, at, "{}"); f.Store.Add(e); return e; }
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS14-AC01: company edit tracks who/when, before-after and drillable source", async f =>
        {
            var initial=await f.Service.Company.GetCompany(f.CompanyId,f.Owner,default);
            var originalName=initial.Name;
            var changed=await f.Service.Company.EditCompany(f.CompanyId,f.Owner,Fixture.Key(),
                CompanyTests.Edit(initial) with { Name = "اسم بعد التدقيق" },default);
            var history=await f.Service.Audit.ListAudit(f.CompanyId,f.Owner,0,10,default,
                actorId:f.Owner.Subject,action:"company.edited",resourceId:f.CompanyId);
            Ensure(history.TotalCount==1 && history.Items.Single().ActorId==f.Owner.Subject);
            var item=await f.Service.Audit.GetAuditEvent(f.CompanyId,history.Items.Single().Id,f.Owner,default);
            using var json=JsonDocument.Parse(item.Details);
            var before=json.RootElement.GetProperty("before");
            var after=json.RootElement.GetProperty("after");
            Ensure(item.ResourceId==f.CompanyId && item.CompanyId==f.CompanyId &&
                item.OccurredAt!=default && before.GetProperty("name").GetString()==originalName &&
                after.GetProperty("name").GetString()=="اسم بعد التدقيق" &&
                (await f.Service.Company.GetCompany(f.CompanyId,f.Owner,default)).Name==
                after.GetProperty("name").GetString() && changed.ResourceId==f.CompanyId);
            var oldCount=f.Store.Count<AuditEvent>();
            await f.Service.Audit.GetAuditEvent(f.CompanyId,item.Id,f.Owner,default);
            Ensure(f.Store.Count<AuditEvent>()==oldCount);
        });
        test("SYS14: UTC day filters include exact boundaries and combine actor action and reference", async f =>
        {
            var resource = Guid.NewGuid(); var day = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);
            Add(f, "User", "test.created", day.AddTicks(-1), resource);
            var first = Add(f, "User", "test.created", day, resource);
            var last = Add(f, "User", "test.created", day.AddDays(1).AddTicks(-1), resource);
            Add(f, "User", "test.created", day.AddDays(1), resource);
            Add(f, "user", "test.created", day, resource); Add(f, "User", "test.CREATED", day, resource);
            Add(f, "User", "test.created", day, Guid.NewGuid());
            var result = await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, new(2025, 1, 2), new(2025, 1, 2), "User", "test.created", resource);
            Ensure(result.TotalCount == 2 && result.Items[0].Id == last.Id && result.Items[1].Id == first.Id);
        });
        test("SYS14-GAP02: omitted and one-sided UTC date filters preserve authorized history", async f =>
        {
            var reference=Guid.NewGuid();
            var before=Add(f,"sys14-date-user","test.optional",
                new DateTimeOffset(2024,2,28,23,59,59,TimeSpan.Zero),reference);
            var inside=Add(f,"sys14-date-user","test.optional",
                new DateTimeOffset(2024,2,29,12,0,0,TimeSpan.Zero),reference);
            var after=Add(f,"sys14-date-user","test.optional",
                new DateTimeOffset(2024,3,1,0,0,0,TimeSpan.Zero),reference);
            var count=f.Store.Count<AuditEvent>();var receipts=f.Store.Count<IdempotencyRecord>();
            async Task<Page<AuditEvent>> Read(DateOnly? from=null,DateOnly? to=null) =>
                await f.Service.Audit.ListAudit(f.CompanyId,f.Owner,0,20,default,
                    from,to,"sys14-date-user","test.optional",reference);
            var all=await Read();
            var lower=await Read(from:new(2024,2,29));
            var upper=await Read(to:new(2024,2,29));
            var exact=await Read(from:new(2024,2,29),to:new(2024,2,29));
            Ensure(all.TotalCount==3 && all.Items.Select(x=>x.Id).ToHashSet().SetEquals(
                new[]{before.Id,inside.Id,after.Id}) &&
                lower.TotalCount==2 && !lower.Items.Any(x=>x.Id==before.Id) &&
                upper.TotalCount==2 && !upper.Items.Any(x=>x.Id==after.Id) &&
                exact.TotalCount==1 && exact.Items.Single().Id==inside.Id &&
                f.Store.Count<AuditEvent>()==count && f.Store.Count<IdempotencyRecord>()==receipts);
        });
        test("SYS14: UTC conversion respects timestamp offsets and maximum calendar date", async f =>
        {
            var id = Guid.NewGuid(); var e = Add(f, "u", "test", new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.FromHours(3)), id);
            Ensure((await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, new(2025, 1, 1), new(2025, 1, 1), resourceId: id)).Items.Single().Id == e.Id);
            var max = Add(f, "u", "test", DateTimeOffset.MaxValue, id);
            Ensure((await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, DateOnly.MaxValue, DateOnly.MaxValue, resourceId: id)).Items.Single().Id == max.Id);
        });
        test("SYS14: historical actors remain searchable without active memberships", async f =>
        {
            var id = Guid.NewGuid(); Add(f, "removed-user", "old.action", DateTimeOffset.UtcNow, id);
            Ensure((await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, actorId: "removed-user", action: "old.action")).TotalCount == 1);
            Ensure((await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, action: "unknown.action")).TotalCount == 0);
        });
        test("SYS14: filtered pages maintain total counts and reads do not create audit events", async f =>
        {
            var id = Guid.NewGuid(); for (var i = 0; i < 3; i++) Add(f, "u", "test.page", DateTimeOffset.UtcNow.AddMinutes(i), id);
            var before = f.Store.Count<AuditEvent>(); var receipts = f.Store.Count<IdempotencyRecord>();
            var page = await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 1, 1, default, resourceId: id);
            Ensure(page.TotalCount == 3 && page.Items.Count == 1);
            Ensure((await f.Service.Audit.GetAuditEvent(f.CompanyId, page.Items.Single().Id, f.Owner, default)).ResourceId == id);
            Ensure((await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 4, 1, default, resourceId: id)).Items.Count == 0);
            Ensure(f.Store.Count<AuditEvent>() == before && f.Store.Count<IdempotencyRecord>() == receipts);
        });
        test("SYS14: audit permission and global scope protect lists and event details across companies", async f =>
        {
            var e = Add(f, "u", "test", DateTimeOffset.UtcNow, Guid.NewGuid());
            await Error("permission_denied", () => f.Service.Audit.GetAuditEvent(f.CompanyId, e.Id, f.Clerk, default));
            await Error("permission_denied", () => f.Service.Audit.ListAudit(f.CompanyId, f.Clerk, 0, 10, default));
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new("scoped-auditor", new[] { Permissions.Audit }, false, new[] { f.BranchId }, null), default);
            await Error("all_branches_required", () => f.Service.Audit.GetAuditEvent(f.CompanyId, e.Id, new("scoped-auditor"), default));
            await Error("all_branches_required", () => f.Service.Audit.ListAudit(f.CompanyId, new("scoped-auditor"), 0, 10, default));
            var foreign = Add(f, "u", "foreign", DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid());
            await Error("reference_not_found", () => f.Service.Audit.GetAuditEvent(f.CompanyId, foreign.Id, f.Owner, default));
            Ensure((await f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, action: "foreign")).TotalCount == 0);
        });
        test("SYS14: reversed dates invalid pages and empty resource identifiers are rejected", async f =>
        {
            await Error("invalid_date_range", () => f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, new(2025, 2, 1), new(2025, 1, 1)));
            await Error("invalid_pagination", () => f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 201, default));
            await Error("invalid_audit_resource", () => f.Service.Audit.ListAudit(f.CompanyId, f.Owner, 0, 10, default, resourceId: Guid.Empty));
        });
    }
    private static void Ensure(bool condition) { if (!condition) throw new Exception("Audit assertion failed"); }
    private static async Task Error(string code, Func<Task> action) { try { await action(); } catch (BusinessException e) when (e.Code == code) { return; } throw new Exception("Expected " + code); }
}
