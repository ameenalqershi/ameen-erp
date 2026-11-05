using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

// SQL integration uses the disposable test database only. Price selection,
// discount precedence, overlap arbitration and invoice effects remain disabled.
public static class SalesPriceListSqlIntegration
{
    public static async Task Upgrade(ErpDbContext db)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "database", "050_sales_price_lists.sql"));
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE dbo.SalesPriceLists ([Conflict] int NOT NULL)");
        await RejectSql("SAL02 pre-existing table conflict",
            db.Database.ExecuteSqlRawAsync(script), 2714);
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=50") == 0,
            "SAL02 failed migration recorded schema version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.SalesPriceLists");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.__AmeenSchemaVersions([Version],[AppliedAt]) VALUES(50,SYSUTCDATETIME())");
        await RejectSql("SAL02 wrong schema owner",
            db.Database.ExecuteSqlRawAsync(script), 51002);
        await db.Database.ExecuteSqlRawAsync("DELETE dbo.__AmeenSchemaVersions WHERE [Version]=50");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);

        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=50") == 1,
            "SAL02 migration replay changed schema version");
        Ensure(await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.SalesPriceLists')") == 14 &&
            await Count(db, "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('dbo.SalesPriceListLines')") == 10,
            "SAL02 SQL field count mismatch");
        Ensure(await Count(db, @"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys
            WHERE parent_object_id IN (OBJECT_ID('dbo.SalesPriceLists'),OBJECT_ID('dbo.SalesPriceListLines'),OBJECT_ID('dbo.CustomerGroups'))
              AND name IN ('FK_SalesPriceList_Company','FK_SalesPriceList_Branch','FK_SalesPriceList_Currency',
                'FK_SalesPriceListLine_Company','FK_SalesPriceListLine_PriceList','FK_SalesPriceListLine_ItemUnit',
                'FK_CustomerGroup_PriceList')") == 7,
            "SAL02 tenant/reference FK count mismatch");
        Ensure(await Count(db, @"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
            WHERE fk.name IN ('FK_SalesPriceList_Branch','FK_SalesPriceList_Currency',
              'FK_SalesPriceListLine_PriceList','FK_SalesPriceListLine_ItemUnit','FK_CustomerGroup_PriceList')
            AND (SELECT COUNT(*) FROM sys.foreign_key_columns fc
                 WHERE fc.constraint_object_id=fk.object_id)=
                 CASE WHEN fk.name='FK_SalesPriceListLine_ItemUnit' THEN 3 ELSE 2 END
            AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fc
                 JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
                 WHERE fc.constraint_object_id=fk.object_id AND c.name='CompanyId')") == 5,
            "SAL02 composite tenant FKs mismatch");
        Ensure(await db.Set<SalesPriceList>().CountAsync() == 0 &&
            await db.Set<SalesPriceListLine>().CountAsync() == 0,
            "SAL02 migration must not seed price lists");

        var listType = db.Model.FindEntityType(typeof(SalesPriceList))
            ?? throw new Exception("SAL02 price-list EF entity missing");
        var lineType = db.Model.FindEntityType(typeof(SalesPriceListLine))
            ?? throw new Exception("SAL02 price-line EF entity missing");
        var groupType = db.Model.FindEntityType(typeof(CustomerGroup))
            ?? throw new Exception("SAL15 group EF entity missing");
        Ensure(listType.GetTableName() == "SalesPriceLists" &&
            lineType.GetTableName() == "SalesPriceListLines",
            "SAL02 EF table mapping mismatch");
        Ensure(listType.FindPrimaryKey()!.Properties.Select(x => x.Name)
            .SequenceEqual(new[] { nameof(SalesPriceList.CompanyId), nameof(SalesPriceList.Id) }) &&
            lineType.FindPrimaryKey()!.Properties.Select(x => x.Name)
            .SequenceEqual(new[] { nameof(SalesPriceListLine.CompanyId), nameof(SalesPriceListLine.Id) }),
            "SAL02 composite tenant keys missing");
        Ensure(listType.FindProperty(nameof(SalesPriceList.Version))!.IsConcurrencyToken &&
            lineType.FindProperty(nameof(SalesPriceListLine.Version))!.IsConcurrencyToken,
            "SAL02 optimistic concurrency token missing");
        Ensure(lineType.FindProperty(nameof(SalesPriceListLine.Price))!.GetPrecision() == 20 &&
            lineType.FindProperty(nameof(SalesPriceListLine.Price))!.GetScale() == 6 &&
            lineType.FindProperty(nameof(SalesPriceListLine.DiscountLimit))!.GetPrecision() == 20 &&
            lineType.FindProperty(nameof(SalesPriceListLine.QuantityThreshold))!.GetScale() == 6,
            "SAL02 price/discount/quantity precision mismatch");
        Ensure(groupType.GetForeignKeys().Any(fk =>
                fk.Properties.Select(p => p.Name).SequenceEqual(
                    new[] { nameof(CustomerGroup.CompanyId), nameof(CustomerGroup.PriceListId) })),
            "SAL15 price-list reference must be same-company");
        var itemUnitFk = lineType.GetForeignKeys().Single(fk =>
            fk.PrincipalEntityType.ClrType == typeof(ItemUnit));
        Ensure(itemUnitFk.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(SalesPriceListLine.CompanyId), nameof(SalesPriceListLine.ItemId), nameof(SalesPriceListLine.UnitId) }) &&
            itemUnitFk.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(
            new[] { nameof(ItemUnit.CompanyId), nameof(ItemUnit.ItemId), nameof(ItemUnit.UnitId) }),
            "SAL02 line must reference an item/unit pair from the same company");
        Console.WriteLine("  SQL PASS SAL02 atomic/idempotent 050 migration, tenant FKs, item-unit pair, EF mapping and no seeds");
    }

    public static async Task<Guid> Guard(ErpDbContext db, Guid companyId, Guid branchId, Actor owner)
    {
        var services = new TestServices(new SqlErpStore(db),
            new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
        var salesAccount = await services.Account.CreateAccount(companyId, owner, Fixture.Key(),
            new("SAL02-SALES", "مبيعات", AccountType.Revenue, null, true, false, false, null, NormalBalance.Credit), default);
        var group = await services.ItemGroup.CreateItemGroup(companyId, owner, Fixture.Key(),
            new("SAL02-GROUP", "مجموعة خدمات", salesAccount.ResourceId), default);
        var unit = await services.Unit.CreateUnit(companyId, owner, Fixture.Key(),
            new("SAL02-U", "وحدة", "EA", 0, "وحدة"), default);
        var item = await services.Item.CreateItem(companyId, owner, Fixture.Key(),
            new("SAL02-ITEM", "خدمة", ItemType.Service, ItemTracking.None, group.ResourceId,
                unit.ResourceId, new[] { new ItemUnitRequest(unit.ResourceId, 1m, "SAL02-BARCODE", 0m) }), default);
        var otherUnit = await services.Unit.CreateUnit(companyId, owner, Fixture.Key(),
            new("SAL02-U2", "وحدة أخرى", "PK", 0, "وحدة"), default);

        var currencyId = await db.Set<Currency>().Where(x => x.CompanyId == companyId && x.Code == "SAR")
            .Select(x => x.Id).SingleAsync();
        var priceListId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var now = new FixedClock().UtcNow;
        var from = new DateOnly(2026, 11, 5);
        var to = new DateOnly(2027, 11, 5);
        db.ChangeTracker.Clear();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT dbo.SalesPriceLists
            (CompanyId,Id,Version,BranchId,Status,Name,CurrencyId,EffectiveFrom,EffectiveTo,IncludesTax,CreatedBy,CreatedAt,Revision,LastModifiedAt)
            VALUES({companyId},{priceListId},{Guid.NewGuid()},{branchId},{"opaque"},{"قائمة أسعار"},
                   {currencyId},{from},{to},{true},{owner.Subject},{now},{1},{now});
            INSERT dbo.SalesPriceListLines
            (CompanyId,Id,Version,PriceListId,Position,ItemId,UnitId,Price,DiscountLimit,QuantityThreshold)
            VALUES({companyId},{lineId},{Guid.NewGuid()},{priceListId},{1},{item.ResourceId},{unit.ResourceId},
                   {12.123456m},{0.000001m},{2.5m})");

        db.ChangeTracker.Clear();
        var list = await db.Set<SalesPriceList>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == priceListId);
        var line = await db.Set<SalesPriceListLine>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == lineId);
        Ensure(list.BranchId == branchId && list.Status == "opaque" && list.Name == "قائمة أسعار" &&
            list.CurrencyId == currencyId && list.EffectiveFrom == from && list.EffectiveTo == to &&
            list.IncludesTax && list.CreatedBy == owner.Subject && list.CreatedAt == now &&
            list.LastModifiedAt == now && list.Revision == 1,
            "SAL02 header SQL/EF roundtrip mismatch");
        Ensure(line.PriceListId == priceListId && line.Position == 1 &&
            line.ItemId == item.ResourceId && line.UnitId == unit.ResourceId &&
            line.Price == 12.123456m && line.DiscountLimit == 0.000001m &&
            line.QuantityThreshold == 2.5m,
            "SAL02 line SQL/EF roundtrip mismatch");

        await RejectSql("SAL02 reversed effective dates",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceLists SET EffectiveTo={from.AddDays(-1)}
              WHERE CompanyId={companyId} AND Id={priceListId}"), 547);
        await RejectSql("SAL02 foreign currency",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceLists SET CurrencyId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={priceListId}"), 547);
        await RejectSql("SAL02 foreign branch",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceLists SET BranchId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={priceListId}"), 547);
        await RejectSql("SAL02 negative price",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceListLines SET Price={-1m}
              WHERE CompanyId={companyId} AND Id={lineId}"), 547);
        await RejectSql("SAL02 negative discount limit",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceListLines SET DiscountLimit={-1m}
              WHERE CompanyId={companyId} AND Id={lineId}"), 547);
        await RejectSql("SAL02 negative quantity threshold",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceListLines SET QuantityThreshold={-1m}
              WHERE CompanyId={companyId} AND Id={lineId}"), 547);
        await RejectSql("SAL02 mismatched item/unit pair",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceListLines SET UnitId={otherUnit.ResourceId}
              WHERE CompanyId={companyId} AND Id={lineId}"), 547);
        await RejectSql("SAL02 missing price-list parent",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              UPDATE dbo.SalesPriceListLines SET PriceListId={Guid.NewGuid()}
              WHERE CompanyId={companyId} AND Id={lineId}"), 547);
        await RejectSql("SAL02 duplicate line position",
            db.Database.ExecuteSqlInterpolatedAsync($@"
              INSERT dbo.SalesPriceListLines
              (CompanyId,Id,Version,PriceListId,Position,ItemId,UnitId,Price,DiscountLimit,QuantityThreshold)
              VALUES({companyId},{Guid.NewGuid()},{Guid.NewGuid()},{priceListId},{1},{item.ResourceId},{unit.ResourceId},{0m},{0m},{null})"),
            2601, 2627);

        db.ChangeTracker.Clear();
        var auditCount = await db.Set<AuditEvent>().CountAsync();
        var outboxCount = await db.Set<OutboxEvent>().CountAsync();
        var journalCount = await db.Set<JournalEntry>().CountAsync();
        var blocked = new SalesPriceList(companyId, branchId, "opaque", "blocked",
            currencyId, from, null, false,
            new[] { (item.ResourceId, unit.ResourceId, 1m, 0m, (decimal?)null) },
            owner.Subject, now);
        db.Add(blocked);
        await RejectBusiness("sales_price_list_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var update = await db.Set<SalesPriceList>().SingleAsync(x => x.CompanyId == companyId && x.Id == priceListId);
        db.Entry(update).State = EntityState.Modified;
        await RejectBusiness("sales_price_list_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var deleteLine = await db.Set<SalesPriceListLine>().SingleAsync(x => x.CompanyId == companyId && x.Id == lineId);
        db.Entry(deleteLine).State = EntityState.Deleted;
        await RejectBusiness("sales_price_list_policy_unavailable", () => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        Ensure(await db.Set<SalesPriceList>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<SalesPriceListLine>().CountAsync(x => x.CompanyId == companyId) == 1 &&
            await db.Set<AuditEvent>().CountAsync() == auditCount &&
            await db.Set<OutboxEvent>().CountAsync() == outboxCount &&
            await db.Set<JournalEntry>().CountAsync() == journalCount,
            "SAL02 denied EF writes changed price list, audit, outbox or ledger");
        Console.WriteLine("  SQL PASS SAL02 field roundtrip, item/unit pair, date/amount constraints and fail-closed EF writes");
        return priceListId;
    }

    private static async Task<int> Count(ErpDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    private static async Task RejectSql(string description, Task action, params int[] numbers)
    {
        try { await action; throw new Exception(description + " unexpectedly succeeded"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => numbers.Contains(x.Number))) { }
    }

    private static async Task RejectBusiness(string code, Func<Task> action)
    {
        try { await action(); throw new Exception(code + " unexpectedly succeeded"); }
        catch (BusinessException e) when (e.Code == code) { }
    }

    private static void Ensure(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
