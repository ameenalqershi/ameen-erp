using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class FoundationSqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner, Actor clerk, CreateJournalRequest journalRequest)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var currencyReceipt = await Call(s => s.Currency.CreateCurrency(cid, owner, Fixture.Key(), new("EUR", 2, "يورو", "Euro", null, "€", "سنت"), default));
        var currency = await Call(s => s.Currency.GetCurrency(cid, currencyReceipt.ResourceId, owner, default));
        var request = CurrencyTests.Edit(currency) with { Scale = 3 }; var key = Fixture.Key();
        var edits = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(s => s.Currency.EditCurrency(cid, currency.Id, owner, key, request, default))));
        Ensure(edits.Distinct().Count() == 1, "Currency concurrent replay differs");
        currency = await Call(s => s.Currency.GetCurrency(cid, currency.Id, owner, default));
        Ensure(currency.Revision == 2 && currency.Scale == 3 && currency.DisplaySymbol == "€", "Currency metadata persistence differs");
        // SYS04-FG03: precision is a persisted integer while the display
        // symbol and fraction are independent UTF-8/Unicode text metadata.
        Ensure(currency.FractionName == "سنت", "SYS04-FG03 fraction name lost during precision edit");
        await using (var currencyFieldsDb = Context())
        {
            var row = await currencyFieldsDb.Set<Currency>().SingleAsync(x =>
                x.CompanyId == cid && x.Id == currencyReceipt.ResourceId);
            Ensure(row.Scale == 3 && row.DisplaySymbol == "€" &&
                row.FractionName == "سنت" && row.Revision == 2,
                "SYS04-FG03 SQL currency precision/symbol/fraction roundtrip differs");
        }
        Console.WriteLine("  SQL PASS SYS04-FG03 precision and Unicode symbol/fraction persisted after edit");
        Ensure((await Call(s => s.Currency.SearchCurrencies(cid, owner, "Euro", true, 0, 10, default))).Items.Single().Id == currency.Id, "Currency search SQL translation differs");
        await Call(s => s.ExchangeRate.CreateExchangeRate(cid, owner, Fixture.Key(), new("EUR", Fixture.Date, 4.2m, "اختبار"), default));
        await Error("currency_precision_in_use", () => Call(s => s.Currency.EditCurrency(cid, currency.Id, owner, Fixture.Key(), CurrencyTests.Edit(currency) with { Scale = 2 }, default)));
        Ensure((await Call(s => s.Currency.GetCurrencyUsage(cid, currency.Id, owner, default))).ExchangeRateCount == 1, "Currency usage SQL differs");
        Console.WriteLine("  SQL PASS SYS04 metadata, concurrent replay, search, usage and precision lock");

        Guid ownerMember;
        await using (var db = Context()) ownerMember = (await db.Set<Membership>().SingleAsync(x => x.CompanyId == cid && x.Subject == owner.Subject)).Id;
        var root = await Call(s => s.CostCenter.CreateCostCenter(cid, owner, Fixture.Key(), new("FND-ROOT", "مركز عام", null, ResponsibleMembershipId: ownerMember), default));
        var childReceipt = await Call(s => s.CostCenter.CreateCostCenter(cid, owner, Fixture.Key(), new("FND-CHILD", "مركز فرعي", journalRequest.BranchId, root.ResourceId, ownerMember, "Branch center"), default));
        var child = await Call(s => s.CostCenter.GetCostCenter(cid, childReceipt.ResourceId, owner, default));
        var centerRequest = CostCenterTests.Edit(child) with { Notes = "ملاحظة محفوظة" }; var centerKey = Fixture.Key();
        var changes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(s => s.CostCenter.EditCostCenter(cid, child.Id, owner, centerKey, centerRequest, default))));
        Ensure(changes.Distinct().Count() == 1, "Center replay differs");
        child = await Call(s => s.CostCenter.GetCostCenter(cid, child.Id, owner, default));
        Ensure(child.Revision == 2 && child.Notes == "ملاحظة محفوظة" && child.ResponsibleMembershipId == ownerMember, "Center metadata persistence differs");
        var searched = await Call(s => s.CostCenter.SearchCostCenters(cid, owner, "Branch", journalRequest.BranchId, root.ResourceId, true, 0, 10, default));
        Ensure(searched.Items.Single().Id == child.Id, "Center scoped search SQL translation differs");
        var centerJournal = journalRequest with { Lines = journalRequest.Lines.Select(x => x with { CostCenterId = child.Id }).ToArray() };
        await Call(s => s.Journal.CreateJournal(cid, clerk, Fixture.Key(), centerJournal, default));
        await Error("center_structure_in_use", () => Call(s => s.CostCenter.EditCostCenter(cid, child.Id, owner, Fixture.Key(), CostCenterTests.Edit(child) with { ParentId = null }, default)));
        var usage = await Call(s => s.CostCenter.GetCostCenterUsage(cid, root.ResourceId, owner, 0, 10, default));
        Ensure(usage.HasUsageInSubtree && usage.JournalCount == 0 && usage.ChildCenterCount == 1, "Center subtree usage differs");
        Console.WriteLine("  SQL PASS COST01 hierarchy, responsibility FK, concurrent replay, scoped search and subtree usage lock");

        var currencyFailureKey = Fixture.Key(); var centerFailureKey = Fixture.Key();
        var currencyChange = CurrencyTests.Edit(currency) with { Notes = "فشل ثم إعادة" };
        var centerChange = CostCenterTests.Edit(child) with { Notes = "فشل ثم إعادة" };
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_FoundationAudit] CHECK ([Action] NOT IN (N'currency.edited',N'cost-center.edited'))");
        try
        {
            await Error("database_constraint", () => Call(s => s.Currency.EditCurrency(cid, currency.Id, owner, currencyFailureKey, currencyChange, default)));
            await Error("database_constraint", () => Call(s => s.CostCenter.EditCostCenter(cid, child.Id, owner, centerFailureKey, centerChange, default)));
            await using var db = Context();
            Ensure((await db.Set<Currency>().SingleAsync(x => x.CompanyId == cid && x.Id == currency.Id)).Version == currency.Version &&
                (await db.Set<CostCenter>().SingleAsync(x => x.CompanyId == cid && x.Id == child.Id)).Version == child.Version, "Failed foundation edit changed cards");
            Ensure(!await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && (x.Key == currencyFailureKey || x.Key == centerFailureKey)), "Failed edits saved receipts");
        }
        finally
        {
            await using var db = Context(); await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_FoundationAudit]");
        }
        await Call(s => s.Currency.EditCurrency(cid, currency.Id, owner, currencyFailureKey, currencyChange, default));
        await Call(s => s.CostCenter.EditCostCenter(cid, child.Id, owner, centerFailureKey, centerChange, default));
        Console.WriteLine("  SQL PASS SYS04/COST01 audit failures roll back cards and receipts; exact retries succeed");

        var otherId = Guid.NewGuid(); await Call(s => s.Company.CreateCompany(owner, Fixture.Key(), new(otherId, "FOUNDATION-OTHER", "أخرى", "YE", "SAR", 2, Fixture.Profile()), default));
        var otherCenter = await Call(s => s.CostCenter.CreateCostCenter(otherId, owner, Fixture.Key(), new("C", "آخر", null), default));
        Guid otherMember;
        await using (var db = Context()) otherMember = (await db.Set<Membership>().SingleAsync(x => x.CompanyId == otherId)).Id;
        foreach (var column in new[] { "ParentId", "ResponsibleMembershipId" })
        {
            await using var db = Context();
            try
            {
                var sql = "UPDATE [CostCenters] SET [" + column + "]=@reference WHERE [CompanyId]=@company AND [Id]=@id";
                await db.Database.ExecuteSqlRawAsync(sql, new SqlParameter("@reference", column == "ParentId" ? otherCenter.ResourceId : otherMember), new SqlParameter("@company", cid), new SqlParameter("@id", child.Id));
                throw new Exception("Cross-company center FK accepted");
            }
            catch (SqlException ex) when (ex.Number == 547) { }
        }
        await using (var restricted = new SqlConnection(connection))
        {
            await restricted.OpenAsync(); await using var command = restricted.CreateCommand();
            command.CommandText = "EXECUTE AS USER = 'ameen_runtime_test'"; await command.ExecuteNonQueryAsync();
            try
            {
                await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(restricted).Options);
                var service = new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock());
                var c = await service.Currency.GetCurrency(cid, currency.Id, owner, default);
                await service.Currency.SetCurrencyStatus(cid, c.Id, owner, Fixture.Key(), new(c.Version, false, "إيقاف"), default);
                var center = await service.CostCenter.GetCostCenter(cid, child.Id, owner, default);
                await service.CostCenter.SetCostCenterStatus(cid, center.Id, owner, Fixture.Key(), new(center.Version, false, "إيقاف"), default);
            }
            finally { command.CommandText = "REVERT"; await command.ExecuteNonQueryAsync(); }
        }
        Console.WriteLine("  SQL PASS COST01 tenant FKs and SYS04/COST01 status writes under runtime role");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Error(string code, Func<Task> work)
    {
        try { await work(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
