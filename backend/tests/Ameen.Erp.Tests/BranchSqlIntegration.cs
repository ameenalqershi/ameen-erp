using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class BranchSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<ErpService, Task<T>> work)
        {
            await using var db = Context(); return await work(new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var parent = await Call(s => s.CreateBranch(cid, owner, Fixture.Key(), new("SQL-PARENT", "أب", "مدينة", "عنوان"), default));
        var child = await Call(s => s.CreateBranch(cid, owner, Fixture.Key(), new("SQL-CHILD", "ابن", "مدينة", "عنوان", parent.ResourceId), default));
        var b = await Call(s => s.GetBranch(cid, child.ResourceId, owner, default));
        var request = BranchTests.Edit(b) with { EnglishName = "SQL branch", Address = "عنوان 001" }; var key = Fixture.Key();
        var replays = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(s => s.EditBranch(cid, b.Id, owner, key, request, default))));
        Ensure(replays.All(x => x == replays[0]), "Concurrent branch replay differs");
        b = await Call(s => s.GetBranch(cid, b.Id, owner, default)); Ensure(b.Revision == 2 && b.Address == "عنوان 001", "Branch revision changed repeatedly");
        Ensure((await Call(s => s.SearchBranches(cid, owner, "SQL branch", parent.ResourceId, true, 0, 10, default))).TotalCount == 1, "Branch SQL search differs");
        request = BranchTests.Edit(b) with { Notes = "retry" }; key = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_BranchAudit] CHECK ([Action] <> 'branch.edited')");
        await Error("database_constraint", () => Call(s => s.EditBranch(cid, b.Id, owner, key, request, default)));
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_BranchAudit]");
            var current = await db.Set<Branch>().SingleAsync(x => x.CompanyId == cid && x.Id == b.Id);
            Ensure(current.Version == b.Version && current.Revision == b.Revision && current.Notes is null, "Failed audit changed branch");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed edit saved receipt");
        }
        await Call(s => s.EditBranch(cid, b.Id, owner, key, request, default));
        // The runtime role must permit new branch columns while audit history stays protected.
        await using (var db = Context())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [branch_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [branch_runtime_test];");
            await db.Database.ExecuteSqlRawAsync("EXECUTE AS USER = 'branch_runtime_test';");
            try
            {
                var service = new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                var current = await service.GetBranch(cid, b.Id, owner, default);
                await service.EditBranch(cid, b.Id, owner, Fixture.Key(), BranchTests.Edit(current) with { Notes = "runtime" }, default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT;"); await db.Database.ExecuteSqlRawAsync("DROP USER [branch_runtime_test];"); }
        }
        var rootA = await Call(s => s.CreateBranch(cid, owner, Fixture.Key(), new("RACE-A", "أ", "مدينة", "عنوان"), default));
        var rootB = await Call(s => s.CreateBranch(cid, owner, Fixture.Key(), new("RACE-B", "ب", "مدينة", "عنوان"), default));
        var a = await Call(s => s.GetBranch(cid, rootA.ResourceId, owner, default));
        var c = await Call(s => s.GetBranch(cid, rootB.ResourceId, owner, default));
        async Task<string> Move(Branch branch, Guid parentId)
        {
            try { await Call(s => s.EditBranch(cid, branch.Id, owner, Fixture.Key(), BranchTests.Edit(branch) with { ParentId = parentId }, default)); return "ok"; }
            catch (BusinessException e) { return e.Code; }
        }
        var moves = await Task.WhenAll(Move(a, c.Id), Move(c, a.Id));
        Ensure(moves.Count(x => x == "ok") == 1 && moves.Count(x => x == "branch_tree_cycle") == 1, "Concurrent moves created a branch cycle");
        // Composite FKs prevent linking a tenant to a different company's parent or center.
        var otherId = Guid.NewGuid();
        await Call(s => s.CreateCompany(owner, Fixture.Key(), new(otherId, "SQL-BRANCH-OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default));
        var otherBranch = await Call(s => s.CreateBranch(otherId, owner, Fixture.Key(), new("B", "فرع", "مدينة", "عنوان"), default));
        var otherCenter = await Call(s => s.CreateCostCenter(otherId, owner, Fixture.Key(), new("C", "مركز", null), default));
        await using (var db = Context())
        {
            foreach (var sql in new[] { "UPDATE [Branches] SET [ParentId]=@foreign WHERE [CompanyId]=@company AND [Id]=@id", "UPDATE [Branches] SET [DefaultCostCenterId]=@foreign WHERE [CompanyId]=@company AND [Id]=@id" })
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql, new Microsoft.Data.SqlClient.SqlParameter("@foreign", sql.Contains("ParentId", StringComparison.Ordinal) ? otherBranch.ResourceId : otherCenter.ResourceId),
                        new Microsoft.Data.SqlClient.SqlParameter("@company", cid), new Microsoft.Data.SqlClient.SqlParameter("@id", b.Id));
                    throw new Exception("Cross-company branch FK succeeded");
                }
                catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Branches] SET [City]=NULL, [Address]=NULL WHERE [CompanyId]={cid} AND [Id]={b.Id}");
        }
        b = await Call(s => s.GetBranch(cid, b.Id, owner, default));
        await Error("branch_profile_incomplete", () => Call(s => s.SetBranchStatus(cid, b.Id, owner, Fixture.Key(), new(b.Version, true, "تفعيل"), default)));
        b = await Call(s => s.GetBranch(cid, b.Id, owner, default));
        await Call(s => s.EditBranch(cid, b.Id, owner, Fixture.Key(), BranchTests.Edit(b) with { City = "مدينة", Address = "عنوان مستكمل" }, default));
        Console.WriteLine("  SQL PASS SYS02 concurrent replay, SQL search, rollback/retry, runtime role, tenant FKs and legacy address completion");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
