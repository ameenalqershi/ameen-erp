using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class ItemGroupSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var sales = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(), new("GROUP-SALES", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default));
        var key = Fixture.Key(); var request = new CreateItemGroupRequest("GROUP-A", "مجموعة", sales.ResourceId, EnglishName: "SQL group");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Call(s => s.ItemGroup.CreateItemGroup(cid, owner, key, request, default))));
        Ensure(results.All(x => x == results[0]), "Group replay differs");
        var a = await Call(s => s.ItemGroup.GetItemGroup(cid, results[0].ResourceId, owner, default));
        var bReceipt = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(), request with { Code = "GROUP-B" }, default));
        var b = await Call(s => s.ItemGroup.GetItemGroup(cid, bReceipt.ResourceId, owner, default));
        async Task<string> Move(ItemGroup group, Guid parent)
        {
            try { await Call(s => s.ItemGroup.EditItemGroup(cid, group.Id, owner, Fixture.Key(), ItemGroupTests.Edit(group) with { ParentId = parent }, default)); return "ok"; }
            catch (BusinessException e) { return e.Code; }
        }
        var moves = await Task.WhenAll(Move(a, b.Id), Move(b, a.Id));
        Ensure(moves.Count(x => x == "ok") == 1 && moves.Count(x => x == "item_group_cycle") == 1, "Concurrent group moves created cycle");
        a = await Call(s => s.ItemGroup.GetItemGroup(cid, a.Id, owner, default));
        var edit = ItemGroupTests.Edit(a) with { Notes = "retry" }; key = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_GroupAudit] CHECK ([Action] <> 'item-group.edited')");
        try { await Call(s => s.ItemGroup.EditItemGroup(cid, a.Id, owner, key, edit, default)); throw new Exception("Expected failure"); }
        catch (BusinessException e) when (e.Code == "database_constraint") { }
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_GroupAudit]");
            var current = await db.Set<ItemGroup>().SingleAsync(x => x.CompanyId == cid && x.Id == a.Id);
            Ensure(current.Version == a.Version && current.Notes is null, "Failed group edit persisted");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == key), "Failed group receipt persisted");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE USER [group_runtime_test] WITHOUT LOGIN; ALTER ROLE [ameen_runtime] ADD MEMBER [group_runtime_test]; EXECUTE AS USER = 'group_runtime_test';");
            try
            {
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                await service.ItemGroup.EditItemGroup(cid, a.Id, owner, key, edit, default);
            }
            finally { await db.Database.ExecuteSqlRawAsync("REVERT; DROP USER [group_runtime_test];"); }
        }
        Ensure((await Call(s => s.ItemGroup.SearchItemGroups(cid, owner, "SQL group", null, true, 0, 10, default))).TotalCount == 2, "Group SQL search differs");
        Ensure((await Call(s => s.Account.GetAccountUsage(cid, sales.ResourceId, owner, 0, 10, default))).HasUsageInSubtree, "Group SQL usage lock missing");
        Ensure((await Call(s => s.ItemGroup.ListAccountItemGroups(cid, sales.ResourceId, owner, 0, 10, default))).TotalCount == 2, "Group SQL account references differ");
        // INV02-AC01: classification and account mappings persist as tenant-constrained
        // references; no unapproved implicit inheritance from parent to child group.
        var stockAccount = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("GROUP-STOCK", "مخزون المجموعة", AccountType.Asset, null, true, true, false, null,
                NormalBalance.Debit), default));
        var costAccount = await Call(s => s.Account.CreateAccount(cid, owner, Fixture.Key(),
            new("GROUP-COST", "تكلفة المجموعة", AccountType.Expense, null, true, false, false, null,
                NormalBalance.Debit), default));
        var unit = await Call(s => s.Unit.CreateUnit(cid, owner, Fixture.Key(),
            new("GROUP-UNIT", "قطعة", "EA", 0, "قطعة"), default));
        var mapped = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(),
            new("GROUP-MAPPED", "مجموعة مخزنية", sales.ResourceId,
                InventoryAccountId: stockAccount.ResourceId, CostAccountId: costAccount.ResourceId), default));
        var goodsRequest = new CreateItemRequest("GROUP-GOODS", "صنف مُصنّف",
            ItemType.Goods, ItemTracking.None, mapped.ResourceId, unit.ResourceId,
            new[] { new ItemUnitRequest(unit.ResourceId, 1, null, null) });
        var goods = await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(), goodsRequest, default));
        await using (var db = Context())
        {
            var classification = await db.Set<ItemGroup>().SingleAsync(x => x.CompanyId == cid && x.Id == mapped.ResourceId);
            var item = await db.Set<Item>().SingleAsync(x => x.CompanyId == cid && x.Id == goods.ResourceId);
            Ensure(classification.SalesAccountId == sales.ResourceId &&
                classification.InventoryAccountId == stockAccount.ResourceId &&
                classification.CostAccountId == costAccount.ResourceId && item.GroupId == classification.Id &&
                item.Type == ItemType.Goods, "SQL INV02-AC01 classification/account mapping differs");
        }
        var childUnmapped = await Call(s => s.ItemGroup.CreateItemGroup(cid, owner, Fixture.Key(),
            new("GROUP-SERVICE", "خدمة", sales.ResourceId, mapped.ResourceId), default));
        await Call(s => s.Item.CreateItem(cid, owner, Fixture.Key(),
            goodsRequest with { Code = "GROUP-SERVICE-ITEM", GroupId = childUnmapped.ResourceId,
                Type = ItemType.Service }, default));
        var rejectedKey = Fixture.Key();
        int priorReceipts, priorAudits, priorItems;
        await using (var db = Context())
        {
            priorReceipts = await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid);
            priorAudits = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.Action == "item.created");
            priorItems = await db.Set<Item>().CountAsync(x => x.CompanyId == cid);
        }
        try
        {
            await Call(s => s.Item.CreateItem(cid, owner, rejectedKey,
                goodsRequest with { Code = "GROUP-MISSING-ACCOUNT", GroupId = childUnmapped.ResourceId }, default));
            throw new Exception("Unapproved parent-account inheritance accepted");
        }
        catch (BusinessException e) when (e.Code == "stock_accounts_required") { }
        await using (var db = Context())
            Ensure(await db.Set<IdempotencyRecord>().CountAsync(x => x.CompanyId == cid) == priorReceipts &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid && x.Action == "item.created") == priorAudits &&
                await db.Set<Item>().CountAsync(x => x.CompanyId == cid) == priorItems &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == rejectedKey),
                "SQL INV02-AC01 denied inherited accounts modified items, audit or receipt");
        Console.WriteLine("  SQL PASS INV02-AC01 classified goods/service account references and no unapproved parent inheritance");
        var other = Guid.NewGuid(); await Call(s => s.Company.CreateCompany(owner, Fixture.Key(), new(other, "GROUP-OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default));
        var foreign = await Call(s => s.Account.CreateAccount(other, owner, Fixture.Key(), new("R", "إيراد", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default));
        await using (var db = Context())
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ItemGroups] SET [SalesAccountId]={foreign.ResourceId} WHERE [CompanyId]={cid} AND [Id]={a.Id}"); throw new Exception("Cross-company group account accepted"); }
            catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547) { }
        }
        Console.WriteLine("  SQL PASS INV02 replay, concurrent cycle prevention, rollback/retry under runtime role, SQL search/account usage and tenant FK");
    }
    private static void Ensure(bool value, string message) { if (!value) throw new Exception(message); }
}
