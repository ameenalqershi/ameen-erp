using Ameen.Erp.Application;
using Ameen.Erp.Domain;

namespace Ameen.Erp.Tests;

public static class BranchTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS02: bilingual address, parent, search and audit roundtrip", async f =>
        {
            var r = await f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("CHILD", "فرع", "صنعاء", "العنوان 001", f.BranchId, null, "Child", "ملاحظة"), default);
            var b = await f.Service.Branch.GetBranch(f.CompanyId, r.ResourceId, f.Owner, default);
            Ensure(b.Revision == 1 && b.City == "صنعاء" && b.Address == "العنوان 001" && b.ParentId == f.BranchId);
            Ensure((await f.Service.Branch.SearchBranches(f.CompanyId, f.Owner, "Child", f.BranchId, true, 0, 20, default)).TotalCount == 1);
            Ensure((await f.Service.Branch.ListBranchAudit(f.CompanyId, b.Id, f.Owner, 0, 20, default)).TotalCount == 1);
        });
        test("SYS02: required address rejects blank without partial creation", async f =>
        {
            await Error("invalid_text", () => f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("BAD", "فرع", "", "عنوان"), default));
            await Error("invalid_text", () => f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("BAD", "فرع", "مدينة", new string('x', 501)), default));
            Ensure(f.Store.Count<Branch>() == 2);
        });
        test("SYS02: self and indirect branch cycles rejected", async f =>
        {
            await Error("branch_tree_cycle", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { ParentId = f.BranchId }, default));
            var other = f.Store.All<Branch>().Single(x => x.Id == f.OtherBranchId);
            await f.Service.Branch.EditBranch(f.CompanyId, other.Id, f.Owner, Fixture.Key(), Edit(other) with { ParentId = f.BranchId }, default);
            await Error("branch_tree_cycle", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { ParentId = f.OtherBranchId }, default));
        });
        test("SYS02: disable parent requires inactive children and activation requires active ancestors", async f =>
        {
            var child = await f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("CHILD", "فرع", "مدينة", "عنوان", f.BranchId), default);
            await Error("active_child_branches", () => Status(f, f.BranchId, false));
            await Status(f, child.ResourceId, false); await Status(f, f.BranchId, false);
            await Error("inactive_parent_branch", () => Status(f, child.ResourceId, true));
            await Status(f, f.BranchId, true); await Status(f, child.ResourceId, true);
        });
        test("SYS02: hierarchy does not grant access to child or parent filters", async f =>
        {
            var child = await f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("CHILD", "فرع", "مدينة", "عنوان", f.BranchId), default);
            Ensure((await f.Service.Branch.SearchBranches(f.CompanyId, f.Limited, null, f.BranchId, null, 0, 20, default)).TotalCount == 0);
            await Error("branch_access_denied", () => f.Service.Branch.GetBranch(f.CompanyId, child.ResourceId, f.Limited, default));
            await Error("branch_access_denied", () => f.Service.Branch.SearchBranches(f.CompanyId, f.Limited, null, f.OtherBranchId, null, 0, 20, default));
            await Error("all_branches_required", () => f.Service.Branch.GetBranchUsage(f.CompanyId, f.BranchId, f.Limited, default));
        });
        test("SYS02: cancelled descendant journal locks ancestor structure but not metadata", async f =>
        {
            var child = await f.Service.Branch.CreateBranch(f.CompanyId, f.Owner, Fixture.Key(), new("CHILD", "فرع", "مدينة", "عنوان", f.BranchId), default);
            await f.Action(await f.Draft(f.Request() with { BranchId = child.ResourceId }), "cancel", reason: "إلغاء");
            await Error("branch_structure_in_use", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { Code = "NEW" }, default));
            await f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { Address = "عنوان جديد" }, default);
            Ensure((await f.Service.Branch.GetBranchUsage(f.CompanyId, f.BranchId, f.Owner, default)).HasSubtreeFinancialUsage);
        });
        test("SYS02: disable after approval blocks posting while historical reports remain", async f =>
        {
            await f.Post();
            var j = await f.Action(await f.Draft(), "submit"); j = await f.Action(j, "approve", f.Approver);
            await Status(f, f.BranchId, false);
            await Error("inactive_branch", () => f.Action(j, "post"));
            Ensure((await f.Service.Report.TrialBalance(f.CompanyId, f.Clerk, Fixture.Date, Fixture.Date, null, default)).TotalClosingDebit == 100);
            await Status(f, f.BranchId, true); await f.Action(j, "post");
        });
        test("SYS02: default center must be active and global or same branch", async f =>
        {
            var foreign = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("OTHER-C", "مركز", f.OtherBranchId), default);
            await Error("invalid_branch_default_center", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { DefaultCostCenterId = foreign.ResourceId }, default));
            var local = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("LOCAL-C", "مركز", f.BranchId), default);
            await f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { DefaultCostCenterId = local.ResourceId }, default);
            Ensure(Get(f).DefaultCostCenterId == local.ResourceId);
            await Error("center_is_branch_default", () => f.Service.CostCenter.SetCostCenterStatus(f.CompanyId, local.ResourceId, f.Owner, Fixture.Key(), new(local.Version, false, "تعطيل"), default));
        });
        test("SYS02 COST01: global default center cannot move outside referencing branch", async f =>
        {
            var center = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("GLOBAL-C", "مركز", null), default);
            await f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { DefaultCostCenterId = center.ResourceId }, default);
            var c = f.Store.All<CostCenter>().Single();
            await Error("center_is_branch_default", () => f.Service.CostCenter.EditCostCenter(f.CompanyId, c.Id, f.Owner, Fixture.Key(),
                new(c.Version, c.Code, c.Name, f.OtherBranchId, c.ParentId, c.ResponsibleMembershipId, c.EnglishName, c.Notes, "نقل"), default));
        });
        test("SYS02: cross-company parent and default center rejected", async f =>
        {
            var cid = Guid.NewGuid(); await f.Service.Company.CreateCompany(f.Owner, Fixture.Key(), new(cid, "OTHERCO", "شركة", "YE", "SAR", 2, Fixture.Profile()), default);
            var branch = await f.Service.Branch.CreateBranch(cid, f.Owner, Fixture.Key(), new("B", "فرع", "مدينة", "عنوان"), default);
            var center = await f.Service.CostCenter.CreateCostCenter(cid, f.Owner, Fixture.Key(), new("C", "مركز", null), default);
            await Error("reference_not_found", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { ParentId = branch.ResourceId }, default));
            await Error("reference_not_found", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { DefaultCostCenterId = center.ResourceId }, default));
        });
        test("SYS02 GL04: inactive template locks structure and disabled branch blocks generation", async f =>
        {
            var member = f.Store.All<Membership>().Single(x => x.Subject == f.Clerk.Subject);
            await f.Service.Membership.GrantMembership(f.CompanyId, f.Owner, Fixture.Key(), new(f.Clerk.Subject, Permissions.All, true, Array.Empty<Guid>(), member.Version), default);
            var template = await f.Service.RecurringJournal.CreateRecurringJournal(f.CompanyId, f.Clerk, Fixture.Key(),
                new(f.BranchId, "قالب", RecurringFrequency.Monthly, RecurringGenerationMode.Manual, new(2026, 1, 1), new(2026, 12, 31),
                    new[] { new RecurringLineRequest(f.CashId, null, "مدين", "SAR", 100, 0), new RecurringLineRequest(f.EquityId, null, "دائن", "SAR", 0, 100) }), default);
            await Status(f, f.BranchId, false);
            var period = f.Store.All<FiscalPeriod>().Single(x => x.StartsOn == new DateOnly(2026, 10, 1));
            await Error("inactive_branch", () => f.Service.RecurringJournal.GenerateRecurringJournal(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(template.Version, period.Id), default));
            await Status(f, f.BranchId, true);
            await f.Service.RecurringJournal.SetRecurringStatus(f.CompanyId, template.ResourceId, f.Clerk, Fixture.Key(), new(template.Version, false, "تعطيل"), default);
            await Error("branch_structure_in_use", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), Edit(Get(f)) with { ParentId = f.OtherBranchId }, default));
            Ensure(f.Store.Count<RecurringJournalGeneration>() == 0 && f.Store.Count<JournalEntry>() == 0);
        });
        test("SYS02: replay, stale version and failed commit preserve revision and audit", async f =>
        {
            var request = Edit(Get(f)) with { Name = "تحديث" }; var key = Fixture.Key(); var audits = f.Store.Count<AuditEvent>();
            f.Store.FailBeforeCommit = true;
            try { await f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, key, request, default); throw new Exception("Expected failure"); } catch (IOException) { }
            Ensure(Get(f).Revision == 1 && Get(f).Version == request.ExpectedVersion && f.Store.Count<AuditEvent>() == audits);
            var r = await f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, key, request, default);
            Ensure(await f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, key, request, default) == r && Get(f).Revision == 2);
            await Error("version_conflict", () => f.Service.Branch.EditBranch(f.CompanyId, f.BranchId, f.Owner, Fixture.Key(), request, default));
        });
    }
    public static EditBranchRequest Edit(Branch b) => new(b.Version, b.Code, b.Name, b.City!, b.Address!, b.ParentId, b.DefaultCostCenterId, b.EnglishName, b.Notes, "تعديل الفرع");
    private static Branch Get(Fixture f) => f.Store.All<Branch>().Single(x => x.Id == f.BranchId);
    private static Task<CommandReceipt> Status(Fixture f, Guid id, bool active)
        => f.Service.Branch.SetBranchStatus(f.CompanyId, id, f.Owner, Fixture.Key(), new(f.Store.All<Branch>().Single(x => x.Id == id).Version, active, "تغيير الحالة"), default);
    private static void Ensure(bool value) { if (!value) throw new Exception("Branch assertion failed"); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
