using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Tests;
public static class WarehouseSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        { await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock())); }
        var branch = await Call(s => s.Branch.CreateBranch(cid, owner, Fixture.Key(), new("WHSQL-B", "فرع المخزن", "مدينة", "عنوان"), default));
        var account = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("WHSQL-A", "مخزون", AccountType.Asset, null, true, true, false, null, NormalBalance.Debit), default));
        var keeper = (await Call(s => s.Membership.ListMembers(cid, owner, 0, 200, default))).Items.Single(x => x.Subject == owner.Subject);
        var request = new CreateWarehouseRequest("WHSQL", "مخزن", branch.ResourceId, keeper.Id, account.ResourceId, WarehouseType.Standard, true,
            new[] { new WarehouseLocationRequest("001", "رف") }, "SQL warehouse");
        var key = Fixture.Key(); var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.Warehouse.CreateWarehouse(cid, owner, key, request, default))));
        Ensure(results.All(x => x == results[0]), "Warehouse replay differs");
        var w = await Call(s => s.Warehouse.GetWarehouse(cid, results[0].ResourceId, owner, default));
        var originalSqlLocationId = w.Locations.Single().Id;
        var edit = WarehouseTests.Edit(w) with { Name = "معدل", Locations = new[] { new WarehouseLocationRequest("001", "رف معدل") } }; key = Fixture.Key();
        results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(s => s.Warehouse.EditWarehouse(cid, w.Id, owner, key, edit, default))));
        Ensure(results.All(x => x == results[0]), "Warehouse edit replay differs");
        w = await Call(s => s.Warehouse.GetWarehouse(cid, w.Id, owner, default));
        Ensure(w.Revision == 2 && w.Locations.Single().Description == "رف معدل" &&
            w.Locations.Single().Id == originalSqlLocationId,
            "SQL location description edit silently replaced its stable identity");
        var preservedLocationId = w.Locations.Single().Id;
        var descriptive = WarehouseTests.Edit(w) with { Name = "اسم SQL جديد" };
        var descriptiveEdit = await Call(s => s.Warehouse.EditWarehouse(cid, w.Id, owner, Fixture.Key(), descriptive, default));
        w = await Call(s => s.Warehouse.GetWarehouse(cid, w.Id, owner, default));
        Ensure(w.Version == descriptiveEdit.Version && w.Name == "اسم SQL جديد" &&
            w.Locations.Single().Id == preservedLocationId,
            "A descriptive SQL edit must preserve the warehouse location primary key");
        Ensure((await Call(s => s.Warehouse.SearchWarehouses(cid, owner, "SQL warehouse", branch.ResourceId, true, 0, 10, default))).TotalCount == 1, "SQL warehouse search differs");
        Ensure((await Call(s => s.Warehouse.ListAccountWarehouses(cid, account.ResourceId, owner, 0, 10, default))).TotalCount == 1, "SQL warehouse account usage differs");
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_WarehouseAudit] CHECK ([Action] <> 'warehouse.edited')");
        edit = WarehouseTests.Edit(w) with { Type = WarehouseType.Quarantine, Locations = new[] { new WarehouseLocationRequest("002", "عزل") } }; key = Fixture.Key();
        try { await Call(s => s.Warehouse.EditWarehouse(cid, w.Id, owner, key, edit, default)); throw new Exception("Expected failure"); } catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_WarehouseAudit]");
            var saved = await db.Set<Warehouse>().SingleAsync(x => x.CompanyId == cid && x.Id == w.Id);
            Ensure(saved.Version == w.Version && saved.Locations.Single().Code == "001", "Failed replacement persisted");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed receipt persisted");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [warehouse_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [warehouse_runtime_test]; EXECUTE AS USER = 'warehouse_runtime_test';");
            try { await new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()).Warehouse.EditWarehouse(cid, w.Id, owner, key, edit, default); }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [warehouse_runtime_test];"); }
        }
        var other = Guid.NewGuid(); await Call(s => s.Company.CreateCompany(owner, Fixture.Key(), new(other, "WHSQL-OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default));
        var foreign = await Call(s => s.Branch.CreateBranch(other, owner, Fixture.Key(), new("B", "فرع", "مدينة", "عنوان"), default));
        await using (var db = Context())
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Warehouses] SET [BranchId]={foreign.ResourceId} WHERE [CompanyId]={cid} AND [Id]={w.Id}"); throw new Exception("Foreign branch accepted"); }
            catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
        }
        Console.WriteLine("  SQL PASS INV01 creation/edit concurrent replay, locations, rollback, runtime role, search, account usage and tenant FK");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
