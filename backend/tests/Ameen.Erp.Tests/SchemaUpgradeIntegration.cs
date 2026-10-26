using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ameen.Erp.Tests;

public static class SchemaUpgradeIntegration
{
    public static async Task Run(ErpDbContext db)
    {
        // Seed old-schema rows, including a posted reversal, without using the new journal columns.
        var f = new Fixture(); await f.Initialize();
        var legacyCenterReceipt = await f.Service.CostCenter.CreateCostCenter(f.CompanyId, f.Owner, Fixture.Key(), new("LEGACY-C", "مركز سابق", f.BranchId), default);
        var legacyCenter = f.Store.All<CostCenter>().Single();
        var original = await f.Post(await f.Draft(CostCenterTests.Request(f, legacyCenterReceipt.ResourceId)));
        var reversal = await f.Service.Journal.ReverseJournal(f.CompanyId, original.ResourceId, f.Clerk, Fixture.Key(),
            new(original.Version, Fixture.Date, "عكس قبل الترقية"), default);
        await f.Post(reversal);
        foreach (var type in new[] { AccountType.Liability, AccountType.Revenue, AccountType.Expense })
            f.Store.Add(new Account(f.CompanyId, "LEGACY-" + type, "حساب سابق", type, null, true, false, false, null,
                type == AccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit));
        var legacyAccounts = f.Store.All<Account>().ToArray();
        var legacyJournals = f.Store.All<JournalEntry>().ToArray();
        var rows = f.Store.All<Company>().Cast<Entity>().Concat(f.Store.All<Currency>()).Concat(f.Store.All<Branch>())
            .Concat(f.Store.All<CostCenter>()).Concat(f.Store.All<FiscalYear>()).Concat(f.Store.All<FiscalPeriod>())
            .Concat(f.Store.All<Account>().OrderBy(x => x.ParentId is null ? 0 : 1)).Concat(legacyJournals)
            .Concat(legacyJournals.SelectMany(x => x.Lines));
        foreach (var entity in rows) await InsertLegacy(db, entity);
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "002_recurring_journals.sql"));

        // A conflicting object forces a mid-upgrade error; every preceding DDL change must roll back.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [RecurringJournalLines] ([TestOnly] int NULL)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(script);
            throw new Exception("Conflicting migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Number == 2714) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=2").AnyAsync(), "Failed upgrade saved version 2");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='RecurringJournalTemplates'").SingleAsync() == 0, "Failed upgrade left partial DDL");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [RecurringJournalLines]");
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        var accountScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "003_account_cards.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Account_NormalBalance] CHECK ([Type] >= 0)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(accountScript);
            throw new Exception("Conflicting account migration unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=3").AnyAsync(), "Failed upgrade saved version 3");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Accounts') AND name IN ('EnglishName','Notes','NormalBalance','Revision')").SingleAsync() == 0, "Failed account upgrade left partial columns");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Accounts] DROP CONSTRAINT [CK_Account_NormalBalance]");
        await db.Database.ExecuteSqlRawAsync(accountScript);
        await db.Database.ExecuteSqlRawAsync(accountScript);
        foreach (var current in await db.Set<Account>().Where(x => x.CompanyId == f.CompanyId).ToListAsync())
        {
            var previous = legacyAccounts.Single(x => x.Id == current.Id);
            Ensure(current.Version == previous.Version && current.Code == previous.Code && current.Type == previous.Type &&
                current.ParentId == previous.ParentId && current.IsActive == previous.IsActive && current.Revision == 1 && current.EnglishName is null && current.Notes is null,
                "Account upgrade changed legacy identity/classification");
            Ensure(current.NormalBalance == (previous.Type is AccountType.Asset or AccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit), "Legacy nature backfill differs");
        }
        var currencyScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "004_currency_cards.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Currencies] ADD CONSTRAINT [CK_Currency_Revision] CHECK ([Scale] >= 0)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(currencyScript);
            throw new Exception("Conflicting currency upgrade unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=4").AnyAsync(), "Failed upgrade saved version 4");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Currencies') AND name='Revision'").SingleAsync() == 0, "Failed currency upgrade left partial DDL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Currencies] DROP CONSTRAINT [CK_Currency_Revision]");
        await db.Database.ExecuteSqlRawAsync(currencyScript); await db.Database.ExecuteSqlRawAsync(currencyScript);
        var legacyCurrency = f.Store.All<Currency>().Single();
        var upgradedCurrency = await db.Set<Currency>().SingleAsync(x => x.CompanyId == f.CompanyId);
        Ensure(upgradedCurrency.Id == legacyCurrency.Id && upgradedCurrency.Version == legacyCurrency.Version &&
            upgradedCurrency.Scale == legacyCurrency.Scale && upgradedCurrency.IsActive && upgradedCurrency.Name == upgradedCurrency.Code && upgradedCurrency.Revision == 1,
            "Currency upgrade changed identity/precision");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 003→004 upgrade preserves currency identity and precision");
        var centerScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "005_cost_center_hierarchy.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [CostCenters] ADD CONSTRAINT [CK_CostCenter_Revision] CHECK ([IsActive] IN (0,1))");
        try
        {
            await db.Database.ExecuteSqlRawAsync(centerScript);
            throw new Exception("Conflicting center upgrade unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=5").AnyAsync(), "Failed upgrade saved version 5");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('CostCenters') AND name='ParentId'").SingleAsync() == 0, "Failed center upgrade left partial DDL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [CostCenters] DROP CONSTRAINT [CK_CostCenter_Revision]");
        await db.Database.ExecuteSqlRawAsync(centerScript); await db.Database.ExecuteSqlRawAsync(centerScript);
        var upgradedCenter = await db.Set<CostCenter>().SingleAsync(x => x.CompanyId == f.CompanyId);
        Ensure(upgradedCenter.Id == legacyCenter.Id && upgradedCenter.Version == legacyCenter.Version && upgradedCenter.BranchId == legacyCenter.BranchId &&
            upgradedCenter.Code == legacyCenter.Code && upgradedCenter.Name == legacyCenter.Name && upgradedCenter.ParentId is null && upgradedCenter.ResponsibleMembershipId is null && upgradedCenter.Revision == 1,
            "Center upgrade changed legacy data");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 004→005 upgrade preserves legacy center and existing journal dimensions");
        var companyScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "006_company_profiles.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Companies] ADD CONSTRAINT [CK_Company_Revision] CHECK ([CurrencyScale] >= 0)");
        try
        {
            await db.Database.ExecuteSqlRawAsync(companyScript);
            throw new Exception("Conflicting company upgrade unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=6").AnyAsync(), "Failed upgrade saved version 6");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Companies') AND name='LegalName'").SingleAsync() == 0, "Failed company upgrade left partial DDL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Companies] DROP CONSTRAINT [CK_Company_Revision]");
        await db.Database.ExecuteSqlRawAsync(companyScript); await db.Database.ExecuteSqlRawAsync(companyScript);
        var previousCompany = f.Store.All<Company>().Single();
        var upgradedCompany = await db.Set<Company>().SingleAsync(x => x.Id == f.CompanyId);
        Ensure(upgradedCompany.Version == previousCompany.Version && upgradedCompany.Code == previousCompany.Code &&
            upgradedCompany.FunctionalCurrency == previousCompany.FunctionalCurrency && upgradedCompany.CurrencyScale == previousCompany.CurrencyScale &&
            upgradedCompany.FiscalYearStartsOn == new DateOnly(2026, 1, 1) && upgradedCompany.IsActive && upgradedCompany.Revision == 1 &&
            !upgradedCompany.IsProfileComplete && upgradedCompany.LegalName is null && upgradedCompany.City is null,
            "Company upgrade changed financial identity or invented legal data");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 005→006 upgrade preserves company and derives calendar without invented legal data");
        var branchScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "007_branch_cards.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Branches] ADD CONSTRAINT [CK_Branch_Revision] CHECK ([IsActive] IN (0,1))");
        try
        {
            await db.Database.ExecuteSqlRawAsync(branchScript);
            throw new Exception("Conflicting branch upgrade unexpectedly succeeded");
        }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=7").AnyAsync(), "Failed upgrade saved version 7");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Branches') AND name='ParentId'").SingleAsync() == 0, "Failed branch upgrade left partial DDL");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Branches] DROP CONSTRAINT [CK_Branch_Revision]");
        await db.Database.ExecuteSqlRawAsync(branchScript); await db.Database.ExecuteSqlRawAsync(branchScript);
        var previousBranch = f.Store.All<Branch>().Single(x => x.Id == f.BranchId);
        var upgradedBranch = await db.Set<Branch>().SingleAsync(x => x.CompanyId == f.CompanyId && x.Id == f.BranchId);
        Ensure(upgradedBranch.Version == previousBranch.Version && upgradedBranch.Code == previousBranch.Code && upgradedBranch.IsActive &&
            upgradedBranch.Revision == 1 && !upgradedBranch.IsProfileComplete && upgradedBranch.ParentId is null && upgradedBranch.DefaultCostCenterId is null,
            "Branch upgrade changed legacy identity or invented address");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 006→007 preserves branch identity without invented address");
        var unitScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "008_units_of_measure.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Branches] ADD CONSTRAINT [CK_Unit_Revision] CHECK ([Revision] > 0)");
        try { await db.Database.ExecuteSqlRawAsync(unitScript); throw new Exception("Conflicting unit upgrade unexpectedly succeeded"); }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=8").AnyAsync(), "Failed upgrade saved version 8");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='UnitsOfMeasure'").SingleAsync() == 0, "Failed unit upgrade left partial table");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Branches] DROP CONSTRAINT [CK_Unit_Revision]");
        await db.Database.ExecuteSqlRawAsync(unitScript); await db.Database.ExecuteSqlRawAsync(unitScript);
        Ensure(!await db.Set<UnitOfMeasure>().AnyAsync(), "Migration invented unit catalog");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 007→008 adds empty unit catalog and preserves prior data");
        var groupScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "009_item_groups.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Branches] ADD CONSTRAINT [CK_ItemGroup_Revision] CHECK ([Revision] > 0)");
        try { await db.Database.ExecuteSqlRawAsync(groupScript); throw new Exception("Conflicting group upgrade succeeded"); }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=9").AnyAsync(), "Failed upgrade saved version 9");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='ItemGroups'").SingleAsync() == 0, "Failed group upgrade left table");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Branches] DROP CONSTRAINT [CK_ItemGroup_Revision]");
        await db.Database.ExecuteSqlRawAsync(groupScript); await db.Database.ExecuteSqlRawAsync(groupScript);
        Ensure(!await db.Set<ItemGroup>().AnyAsync(), "Migration invented groups");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 008→009 preserves prior data and adds empty item groups");
        var itemScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "010_items_and_units.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [ItemUnits] ([TestOnly] int NULL)");
        try { await db.Database.ExecuteSqlRawAsync(itemScript); throw new Exception("Conflicting item upgrade succeeded"); }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=10").AnyAsync(), "Failed upgrade saved version 10");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='Items'").SingleAsync() == 0, "Failed item upgrade left table");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [ItemUnits]");
        await db.Database.ExecuteSqlRawAsync(itemScript); await db.Database.ExecuteSqlRawAsync(itemScript);
        Ensure(!await db.Set<Item>().AnyAsync(), "Migration invented items");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 009→010 preserves prior data and adds item/unit tables");
        var warehouseScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "011_warehouses.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [WarehouseLocations] ([TestOnly] int NULL)");
        try { await db.Database.ExecuteSqlRawAsync(warehouseScript); throw new Exception("Conflicting warehouse upgrade succeeded"); }
        catch (SqlException ex) when (ex.Errors.Cast<SqlError>().Any(e => e.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM dbo.__AmeenSchemaVersions WHERE [Version]=11").AnyAsync(), "Failed upgrade saved version 11");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='Warehouses'").SingleAsync() == 0, "Failed warehouse upgrade left table");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [WarehouseLocations]");
        await db.Database.ExecuteSqlRawAsync(warehouseScript); await db.Database.ExecuteSqlRawAsync(warehouseScript);
        Ensure(!await db.Set<Warehouse>().AnyAsync(), "Migration invented warehouses");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 010→011 adds empty warehouses and locations, preserving prior data");
        var identityScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "012_inventory_identities.sql"));
        await db.Database.ExecuteSqlRawAsync(identityScript);
        await db.Database.ExecuteSqlRawAsync(identityScript);
        Ensure(!await db.Set<InventoryIdentity>().AnyAsync(), "Identity upgrade invented rows");
        Console.WriteLine("  SQL PASS idempotent schema 011→012 adds empty inventory identities");
        var numberScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "013_inventory_identity_numbers.sql"));
        await db.Database.ExecuteSqlRawAsync(numberScript);
        await db.Database.ExecuteSqlRawAsync(numberScript);
        Console.WriteLine("  SQL PASS idempotent schema 012→013 separates inventory numbers");
        var openingScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "014_inventory_openings.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [InventoryOpeningLines] ([TestOnly] int NULL)");
        try { await db.Database.ExecuteSqlRawAsync(openingScript); throw new Exception("Conflicting opening upgrade accepted"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name='InventoryOpenings'").SingleAsync() == 0, "Failed opening upgrade left header table");
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=14").AnyAsync(), "Failed opening upgrade saved version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [InventoryOpeningLines]");
        await db.Database.ExecuteSqlRawAsync(openingScript); await db.Database.ExecuteSqlRawAsync(openingScript);
        Ensure(!await db.Set<InventoryOpening>().AnyAsync(), "Opening upgrade invented balances");
        Console.WriteLine("  SQL PASS idempotent schema 013→014 adds empty inventory opening documents");
        var postingScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "015_inventory_opening_posting.sql"));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [StockMovements] ([TestOnly] int NULL)");
        try { await db.Database.ExecuteSqlRawAsync(postingScript); throw new Exception("Conflicting posting upgrade accepted"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Journals') AND name='InventoryOpeningId'").SingleAsync() == 0, "Failed posting migration left journal source column");
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=15").AnyAsync(), "Failed posting migration saved version");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [StockMovements]");
        await db.Database.ExecuteSqlRawAsync(postingScript); await db.Database.ExecuteSqlRawAsync(postingScript);
        Ensure(!await db.Set<StockMovement>().AnyAsync(), "Posting upgrade invented balances");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 014→015 creates no stock or settings and preserves old journals");
        var reversalScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "016_inventory_opening_reversals.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [InventoryOpenings] ADD CONSTRAINT [CK_InventoryOpening_Reversal] CHECK ([Revision] > 0)");
        try { await db.Database.ExecuteSqlRawAsync(reversalScript); throw new Exception("Conflicting reversal migration accepted"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('StockMovements') AND name='ReversesMovementId'").SingleAsync() == 0, "Failed reversal migration left columns");
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=16").AnyAsync(), "Failed reversal migration saved version");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [InventoryOpenings] DROP CONSTRAINT [CK_InventoryOpening_Reversal]");
        await db.Database.ExecuteSqlRawAsync(reversalScript); await db.Database.ExecuteSqlRawAsync(reversalScript);
        Ensure(!await db.Set<StockMovement>().AnyAsync(), "Reversal upgrade invented movements");
        Console.WriteLine("  SQL PASS idempotent schema 015→016 adds reversal provenance without changing old amounts");





        var generalOpeningScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "017_general_openings.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Journals] ADD CONSTRAINT [CK_Journal_GeneralOpening] CHECK ([Sequence] > 0)");
        try { await db.Database.ExecuteSqlRawAsync(generalOpeningScript); throw new Exception("Conflicting general opening upgrade accepted"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id=OBJECT_ID('Journals') AND name='OperationStartsOn'").SingleAsync() == 0, "Failed opening upgrade left columns");
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=17").AnyAsync(), "Failed general opening migration saved version");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Journals] DROP CONSTRAINT [CK_Journal_GeneralOpening]");
        await db.Database.ExecuteSqlRawAsync(generalOpeningScript); await db.Database.ExecuteSqlRawAsync(generalOpeningScript);
        Console.WriteLine("  SQL PASS atomic/idempotent schema 016→017 preserves journal provenance and adds opening metadata");
        var foreignOpeningScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "018_general_opening_foreign_currency.sql"));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Journals] ADD CONSTRAINT [CK_OpeningFx_TestCollision] CHECK ([Sequence] > 0)");
        var collisionScript = foreignOpeningScript.Replace("[CK_Journal_GeneralOpening] CHECK (",
            "[CK_OpeningFx_TestCollision] CHECK (");
        try { await db.Database.ExecuteSqlRawAsync(collisionScript); throw new Exception("Conflicting FX upgrade unexpectedly succeeded"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=18").AnyAsync(),
            "Failed FX upgrade saved schema version");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name='CK_Journal_GeneralOpening'").SingleAsync() == 1,
            "Failed FX upgrade did not roll back old opening protection");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Journals] DROP CONSTRAINT [CK_OpeningFx_TestCollision]");
        await db.Database.ExecuteSqlRawAsync(foreignOpeningScript);
        await db.Database.ExecuteSqlRawAsync(foreignOpeningScript);
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=18").SingleAsync() == 1,
            "FX upgrade should apply exactly once");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 017→018 opens dated FX with rollback and no historical changes");

        var scheduleScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "019_recurring_schedule.sql"));
        // Force an object-name collision in the ADD step. The old mode=0 constraint must return on rollback.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [RecurringJournalTemplates] ADD CONSTRAINT [CK_Scheduler_Collision] CHECK ([GenerationMode] = 0)");
        var brokenScheduleScript = scheduleScript.Replace(
            "ADD CONSTRAINT [CK_Recurring_Mode]", "ADD CONSTRAINT [CK_Scheduler_Collision]");
        try { await db.Database.ExecuteSqlRawAsync(brokenScheduleScript); throw new Exception("Conflicting scheduler migration accepted"); }
        catch (SqlException e) when (e.Errors.Cast<SqlError>().Any(x => x.Number == 2714)) { }
        Ensure(!await db.Database.SqlQueryRaw<int>("SELECT [Version] AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=19").AnyAsync(),
            "Failed scheduler migration recorded version");
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name='CK_Recurring_Mode'").SingleAsync() == 1,
            "Failed scheduler migration did not restore original constraint");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [RecurringJournalTemplates] DROP CONSTRAINT [CK_Scheduler_Collision]");
        await db.Database.ExecuteSqlRawAsync(scheduleScript);
        await db.Database.ExecuteSqlRawAsync(scheduleScript);
        Ensure(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [__AmeenSchemaVersions] WHERE [Version]=19").SingleAsync() == 1,
            "Scheduler migration was not idempotent");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 018→019 enables scheduled drafts, rolls back failures and preserves old modes");

        var loaded = await db.Set<JournalEntry>().Where(x => x.CompanyId == f.CompanyId).ToListAsync();
        foreach (var previous in legacyJournals)
        {
            var current = loaded.Single(x => x.Id == previous.Id);
            Ensure(current.Source == (previous.ReversesJournalId is null ? JournalSource.Manual : JournalSource.Reversal), "Legacy source backfill differs");
            Ensure(current.Lines.OrderBy(x => x.Position).Select(x => x.CostCenterId)
                .SequenceEqual(previous.Lines.OrderBy(x => x.Position).Select(x => x.CostCenterId)), "Upgrade changed journal cost-center dimensions");
            Ensure(current.Version == previous.Version && current.State == previous.State && current.AccountingDate == previous.AccountingDate, "Upgrade changed financial identity/state");
            Ensure(current.Lines.Sum(x => x.FunctionalDebit) == previous.Lines.Sum(x => x.FunctionalDebit) &&
                current.Lines.Sum(x => x.FunctionalCredit) == previous.Lines.Sum(x => x.FunctionalCredit), "Upgrade changed financial amounts");
        }
        Console.WriteLine("  SQL PASS atomic/idempotent schema 002→003 upgrade preserves account identity and backfills all five classifications");
        db.ChangeTracker.Clear();
        foreach (var table in new[] { "JournalLines", "Journals", "Accounts", "CostCenters", "FiscalPeriods", "FiscalYears", "Branches", "Currencies" })
        {
            // Table identifiers come only from this fixed test list; values remain parameters.
            var sql = "DELETE FROM [" + table + "] WHERE [CompanyId]=@company";
            await db.Database.ExecuteSqlRawAsync(sql, new SqlParameter("@company", f.CompanyId));
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Companies] WHERE [Id]={f.CompanyId}");
        Console.WriteLine("  SQL PASS atomic/idempotent schema 001→002 upgrade preserves posted amounts, versions and reversal provenance");
    }

    private static async Task InsertLegacy(ErpDbContext db, Entity entity)
    {
        var type = db.Model.FindEntityType(entity.GetType())!;
        var table = type.GetTableName()!;
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@table",
            new SqlParameter("@table", table)).ToListAsync();
        var tableId = StoreObjectIdentifier.Table(table, null);
        var properties = type.GetProperties().Where(p => columns.Contains(p.GetColumnName(tableId)!)).ToArray();
        var parameters = properties.Select((p, i) =>
        {
            var value = p.PropertyInfo!.GetValue(entity);
            value = p.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value;
            if (value is Enum e) value = Convert.ToInt32(e);
            return new SqlParameter("@p" + i, value ?? DBNull.Value);
        }).ToArray();
        var sql = $"INSERT INTO [{table}] (" + string.Join(",", properties.Select(p => "[" + p.GetColumnName(tableId) + "]")) +
            ") VALUES (" + string.Join(",", parameters.Select(p => p.ParameterName)) + ")";
        await db.Database.ExecuteSqlRawAsync(sql, parameters.Cast<object>());
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
}
