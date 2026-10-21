using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class CostCenterTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("COST01: bilingual hierarchy and responsible member roundtrip with scoped search", async f =>
        {
            var parent = await Create(f, "ROOT", null); var member = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            var child = await f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("001", "المبيعات", f.BranchId, parent.ResourceId, member.Id, "Sales", "ملاحظة"), default);
            var c = await Get(f, child); Ensure(c.Code == "001" && c.Revision == 1 && c.ResponsibleMembershipId == member.Id);
            var page = await f.Service.SearchCostCenters(f.CompanyId, f.Limited, "Sales", null, parent.ResourceId, true, 0, 10, default);
            Ensure(page.Items.Single().Id == c.Id);
        });
        test("COST01: direct and indirect cycles are rejected atomically", async f =>
        {
            var root = await Create(f, "ROOT", null); var child = await Create(f, "CHILD", null, root.ResourceId); var r = await Get(f, root);
            foreach (var parent in new[] { root.ResourceId, child.ResourceId })
                await Error("center_tree_cycle", () => f.Service.EditCostCenter(f.CompanyId, r.Id, f.Owner, Fixture.Key(), Edit(r) with { ParentId = parent }, default));
            Ensure((await Get(f, root)).Version == root.Version);
        });
        test("COST01: branch parent cannot own a global or another-branch child", async f =>
        {
            var root = await Create(f, "ROOT", f.BranchId);
            await Error("center_branch_mismatch", () => Create(f, "GLOBAL", null, root.ResourceId));
            await Error("center_branch_mismatch", () => Create(f, "OTHER", f.OtherBranchId, root.ResourceId));
            await Create(f, "VALID", f.BranchId, root.ResourceId);
        });
        test("COST01: parent branch narrowing cannot hide existing global or other-branch descendants", async f =>
        {
            var root = await Create(f, "ROOT", null); await Create(f, "OTHER", f.OtherBranchId, root.ResourceId);
            var c = await Get(f, root);
            await Error("center_branch_mismatch", () => f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { BranchId = f.BranchId }, default));
        });
        test("COST01: only active same-company members with center scope can be responsible", async f =>
        {
            var limited = f.Store.All<Membership>().Single(x => x.Subject == f.Limited.Subject);
            await Error("invalid_center_responsible", () => f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("GLOBAL", "عام", null, ResponsibleMembershipId: limited.Id), default));
            await Error("invalid_center_responsible", () => f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("OTHER", "آخر", f.OtherBranchId, ResponsibleMembershipId: limited.Id), default));
            await f.Service.SetMembershipStatus(f.CompanyId, limited.Id, f.Owner, Fixture.Key(), new(limited.Version, false, "إيقاف المسؤول"), default);
            await Error("invalid_center_responsible", () => f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("OFF", "معطل", f.BranchId, ResponsibleMembershipId: limited.Id), default));
            await Error("reference_not_found", () => f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("BAD", "غير موجود", f.BranchId, ResponsibleMembershipId: Guid.NewGuid()), default));
        });
        test("COST01: disable parent requires inactive children; reactivate child requires active parent", async f =>
        {
            var root = await Create(f, "ROOT", null); var child = await Create(f, "CHILD", null, root.ResourceId);
            await Error("active_child_centers", () => Status(f, root, false));
            child = await Status(f, child, false); root = await Status(f, root, false);
            await Error("inactive_parent_center", () => Status(f, child, true));
            await Error("inactive_parent_center", () => Create(f, "NEW", null, root.ResourceId));
            root = await Status(f, root, true); await Status(f, child, true);
        });
        test("COST01: usage in cancelled journals locks own and ancestor structure", async f =>
        {
            var root = await Create(f, "ROOT", null); var child = await Create(f, "CHILD", f.BranchId, root.ResourceId);
            var draft = await f.Draft(Request(f, child.ResourceId)); await f.Action(draft, "cancel", reason: "إلغاء");
            foreach (var receipt in new[] { root, child })
            {
                var c = await Get(f, receipt);
                await Error("center_structure_in_use", () => f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { Code = "CHANGED" }, default));
            }
            var usage = await f.Service.GetCostCenterUsage(f.CompanyId, root.ResourceId, f.Owner, 0, 10, default);
            Ensure(usage.HasUsageInSubtree && usage.JournalCount == 0 && usage.ChildCenterCount == 1);
            usage = await f.Service.GetCostCenterUsage(f.CompanyId, child.ResourceId, f.Owner, 0, 10, default);
            Ensure(usage.JournalCount == 1 && usage.References.Items.Single().State == "Cancelled");
        });
        test("COST01: unused center can move between compatible parents with audit and revision", async f =>
        {
            var one = await Create(f, "ONE", null); var two = await Create(f, "TWO", null); var child = await Create(f, "CHILD", f.BranchId, one.ResourceId);
            var c = await Get(f, child);
            var r = await f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, Fixture.Key(), Edit(c) with { ParentId = two.ResourceId, EnglishName = "Moved" }, default);
            c = await Get(f, r); Ensure(c.ParentId == two.ResourceId && c.Revision == 2);
            Ensure((await f.Service.ListCostCenterAudit(f.CompanyId, c.Id, f.Owner, 0, 10, default)).TotalCount == 2);
        });
        test("COST01: inactive center blocks posting while reports preserve past movement", async f =>
        {
            var center = await Create(f, "C", f.BranchId); await f.Post(await f.Draft(Request(f, center.ResourceId)));
            var draft = await f.Draft(Request(f, center.ResourceId)); var approved = await f.Action(await f.Action(draft, "submit"), "approve", f.Approver);
            center = await Status(f, center, false);
            await Error("cost_center_mismatch", () => f.Action(approved, "post"));
            var report = await f.Service.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default, center.ResourceId);
            Ensure(report.TotalClosingDebit == 100);
            center = await Status(f, center, true); await f.Action(approved, "post");
        });
        test("COST01: global card visibility never exposes another branch card or its usage", async f =>
        {
            var global = await Create(f, "GLOBAL", null); var other = await Create(f, "OTHER", f.OtherBranchId, global.ResourceId);
            var page = await f.Service.SearchCostCenters(f.CompanyId, f.Limited, null, null, null, null, 0, 20, default);
            Ensure(page.Items.Any(x => x.Id == global.ResourceId) && page.Items.All(x => x.Id != other.ResourceId));
            await Error("branch_access_denied", () => f.Service.GetCostCenter(f.CompanyId, other.ResourceId, f.Limited, default));
            await Error("branch_access_denied", () => f.Service.SearchCostCenters(f.CompanyId, f.Limited, null, null, other.ResourceId, null, 0, 10, default));
            await Error("all_branches_required", () => f.Service.GetCostCenterUsage(f.CompanyId, global.ResourceId, f.Limited, 0, 10, default));
        });
        test("COST01: same-key edit replays, stale version fails and rollback keeps revision", async f =>
        {
            var r = await Create(f, "C", null); var c = await Get(f, r); var request = Edit(c) with { Notes = "جديد" }; var key = Fixture.Key();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, key, request, default); throw new Exception("Expected failure"); }
            catch (IOException) { }
            Ensure((await Get(f, r)).Revision == 1 && (await Get(f, r)).Notes is null);
            var changed = await f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, key, request, default);
            Ensure(await f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, key, request, default) == changed);
            await Error("version_conflict", () => f.Service.EditCostCenter(f.CompanyId, c.Id, f.Owner, Fixture.Key(), request, default));
            Ensure((await Get(f, r)).Revision == 2);
        });
        test("COST01: foreign-company parent and responsible member cannot be referenced", async f =>
        {
            var cid = Guid.NewGuid(); await f.Service.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHER", "أخرى", "YE", "SAR", 2), default);
            var parent = await f.Service.CreateCostCenter(cid, f.Owner, Fixture.Key(), new("C", "آخر", null), default);
            var member = f.Store.All<Membership>().Single(x => x.CompanyId == cid);
            await Error("reference_not_found", () => Create(f, "BAD", null, parent.ResourceId));
            await Error("reference_not_found", () => f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("BAD", "آخر", null, ResponsibleMembershipId: member.Id), default));
        });
    }
    public static EditCostCenterRequest Edit(CostCenter c) => new(c.Version, c.Code, c.Name, c.BranchId, c.ParentId, c.ResponsibleMembershipId, c.EnglishName, c.Notes, "تحديث المركز");
    private static Task<CommandReceipt> Create(Fixture f, string code, Guid? branch, Guid? parent = null) =>
        f.Service.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new(code, "مركز " + code, branch, parent), default);
    private static Task<CostCenter> Get(Fixture f, CommandReceipt r) => f.Service.GetCostCenter(f.CompanyId, r.ResourceId, f.Owner, default);
    private static Task<CommandReceipt> Status(Fixture f, CommandReceipt r, bool active) => f.Service.SetCostCenterStatus(f.CompanyId, r.ResourceId, f.Owner, Fixture.Key(), new(r.Version, active, "تغيير الحالة"), default);
    public static CreateJournalRequest Request(Fixture f, Guid center) => f.Request() with { Lines = f.Request().Lines.Select(x => x with { CostCenterId = center }).ToArray() };
    private static void Ensure(bool condition) { if (!condition) throw new Exception("Cost center assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
