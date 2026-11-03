using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class CompanySqlIntegration
{
    public static async Task Run(string connection, Guid cid, Actor owner)
    {
        ErpDbContext Context() => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(connection).Options);
        async Task<T> Call<T>(Func<TestServices, Task<T>> work)
        {
            await using var db = Context(); return await work(new TestServices(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var company = await Call(s => s.Company.GetCompany(cid, owner, default));
        var request = CompanyTests.Edit(company) with { Profile = CompanyTests.Profile(company) with { City = "مدينة SQL", CommercialRegistration = "0000123", Notes = "دليل" } };
        var key = Fixture.Key();
        var edits = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(s => s.Company.EditCompany(cid, owner, key, request, default))));
        Ensure(edits.Distinct().Count() == 1, "Company concurrent replay differs");
        company = await Call(s => s.Company.GetCompany(cid, owner, default));
        Ensure(company.Id == cid && company.Code == "SQLTEST" && company.Name == "اختبار SQL" &&
            company.Revision == 2 && company.CommercialRegistration == "0000123" && company.City == "مدينة SQL" &&
            company.Notes == "دليل" && company.IsProfileComplete, "SYS01-FG01/FG02 company profile SQL differs");
        Ensure((await Call(s => s.Company.ListCompanyAudit(cid, owner, 0, 20, default))).Items.Count(x => x.Action == "company.edited") == 1, "Company replay duplicated audit");
        // SYS01-FG06: audit fields are server-generated events, not editable
        // company profile fields. Validate the actual persisted SQL rows.
        await using (var auditDb = Context())
        {
            var events = await auditDb.Set<AuditEvent>()
                .Where(x => x.CompanyId == cid && x.ResourceId == cid &&
                    (x.Action == "company.created" || x.Action == "company.edited"))
                .ToArrayAsync();
            var created = events.Single(x => x.Action == "company.created");
            var edited = events.Single(x => x.Action == "company.edited");
            Ensure(events.Length == 2 &&
                created.ActorId == owner.Subject && edited.ActorId == owner.Subject &&
                created.OccurredAt != default && edited.OccurredAt >= created.OccurredAt &&
                created.Id != edited.Id && company.Revision == 2,
                "SYS01-FG06 persisted creator, timestamps or revision are inconsistent");
        }
        Console.WriteLine("  SQL PASS SYS01-FG06 company creator, creation time, revision and modification audit");
        // SYS01-OP03: usage is a read-only, SQL-derived tenant snapshot.
        var usage = await Call(s => s.Company.GetCompanyUsage(cid, owner, default));
        await using (var usageDb = Context())
            Ensure(usage.CompanyId == cid &&
                usage.BranchCount == await usageDb.Set<Branch>().CountAsync(x => x.CompanyId == cid) &&
                usage.AccountCount == await usageDb.Set<Account>().CountAsync(x => x.CompanyId == cid) &&
                usage.CurrencyCount == await usageDb.Set<Currency>().CountAsync(x => x.CompanyId == cid) &&
                usage.FiscalYearCount == await usageDb.Set<FiscalYear>().CountAsync(x => x.CompanyId == cid) &&
                usage.JournalCount == await usageDb.Set<JournalEntry>().CountAsync(x => x.CompanyId == cid) &&
                usage.RecurringTemplateCount == await usageDb.Set<RecurringJournalTemplate>().CountAsync(x => x.CompanyId == cid) &&
                usage.ExchangeRateCount == await usageDb.Set<ExchangeRate>().CountAsync(x => x.CompanyId == cid) &&
                !usage.CanChangeFunctionalCurrency,
                "SYS01-OP03 usage differs from persisted tenant-scoped SQL");
        var auditPage = await Call(s => s.Company.ListCompanyAudit(cid, owner, 0, 1, default));
        Ensure(auditPage.TotalCount == 2 && auditPage.Items.Count == 1 &&
            auditPage.Items[0].CompanyId == cid && auditPage.Items[0].ResourceId == cid,
            "SYS01-OP03 company audit pagination or tenant filter differs");
        await Error("permission_denied", () => Call(s => s.Company.GetCompanyUsage(cid, new Actor("external"), default)));
        await Error("permission_denied", () => Call(s => s.Company.ListCompanyAudit(cid, new Actor("external"), 0, 1, default)));
        Console.WriteLine("  SQL PASS SYS01-OP03 authoritative usage counts, audit pagination and tenant isolation");
        Console.WriteLine("  SQL PASS SYS01 profile, nullable date, leading zeros and concurrent edit replay");

        // SYS01-AC02: a persisted journal permanently blocks an unapproved functional
        // currency switch, including when the journal was later cancelled.
        await Call(s => s.Currency.CreateCurrency(cid, owner, Fixture.Key(),
            new("NOK", 2, "كرونة نرويجية"), default));
        var beforeConversion = await Call(s => s.Company.GetCompany(cid, owner, default));
        var conversionKey = Fixture.Key();
        int conversionAuditCount;
        await using (var db = Context())
            conversionAuditCount = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
        await Error("functional_currency_in_use", () => Call(s => s.Company.EditCompany(cid, owner,
            conversionKey, CompanyTests.Edit(beforeConversion) with { FunctionalCurrency = "NOK" }, default)));
        await using (var db = Context())
        {
            var persisted = await db.Set<Company>().SingleAsync(x => x.Id == cid);
            Ensure(persisted.FunctionalCurrency == beforeConversion.FunctionalCurrency &&
                persisted.CurrencyScale == beforeConversion.CurrencyScale &&
                persisted.Revision == beforeConversion.Revision &&
                persisted.Version == beforeConversion.Version &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == conversionAuditCount &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == conversionKey),
                "SYS01-AC02 blocked conversion changed SQL company, audit or receipt");
        }
        Console.WriteLine("  SQL PASS SYS01-AC02 post-journal functional currency change is rejected atomically");

        var failureRequest = CompanyTests.Edit(company) with { Name = "فشل ثم إعادة" }; var failureKey = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_CompanyAudit] CHECK ([Action] <> N'company.edited')");
        try
        {
            await Error("database_constraint", () => Call(s => s.Company.EditCompany(cid, owner, failureKey, failureRequest, default)));
            await using var db = Context();
            var saved = await db.Set<Company>().SingleAsync(x => x.Id == cid);
            Ensure(saved.Version == company.Version && saved.Revision == company.Revision && saved.Name == company.Name &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == failureKey), "Failed company edit leaked changes");
        }
        finally { await using var db = Context(); await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_CompanyAudit]"); }
        await Call(s => s.Company.EditCompany(cid, owner, failureKey, failureRequest, default));
        Console.WriteLine("  SQL PASS SYS01 audit failure rolls back company revision, fields and receipt; exact retry succeeds");

        company = await Call(s => s.Company.GetCompany(cid, owner, default)); var completeProfile = CompanyTests.Profile(company);
        await using (var db = Context()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Companies] SET [LegalName]=NULL WHERE [Id]={cid}");
        var incomplete = await Call(s => s.Company.GetCompany(cid, owner, default));
        Ensure(!incomplete.IsProfileComplete, "Legacy incomplete profile not detected");
        await Error("company_profile_incomplete", () => Call(s => s.Branch.CreateBranch(cid, owner, Fixture.Key(), new("PROFILE-BLOCKED", "محجوب", "مدينة الاختبار", "عنوان الاختبار"), default)));
        await Call(s => s.Company.EditCompany(cid, owner, Fixture.Key(), new(incomplete.Version, incomplete.Code, incomplete.Name, incomplete.CountryCode,
            incomplete.FunctionalCurrency, completeProfile, "استكمال بيانات قديمة"), default));
        var branchKey = Fixture.Key(); var branchRequest = new CreateBranchRequest("COMPANY-STATUS", "اختبار الحالة", "مدينة الاختبار", "عنوان الاختبار");
        var branch = await Call(s => s.Branch.CreateBranch(cid, owner, branchKey, branchRequest, default));
        company = await Call(s => s.Company.GetCompany(cid, owner, default));
        var off = await Call(s => s.Company.SetCompanyStatus(cid, owner, Fixture.Key(), new(company.Version, false, "تعطيل"), default));
        await Error("company_inactive", () => Call(s => s.Branch.CreateBranch(cid, owner, branchKey, branchRequest, default)));
        Ensure(!(await Call(s => s.Company.GetCompany(cid, owner, default))).IsActive, "Inactive company read failed");
        await Call(s => s.Company.SetCompanyStatus(cid, owner, Fixture.Key(), new(off.Version, true, "تفعيل"), default));
        Ensure(await Call(s => s.Branch.CreateBranch(cid, owner, branchKey, branchRequest, default)) == branch, "Reactivated command replay differs");
        // SYS01-OP02: an audit persistence error must roll back the status,
        // concurrency token, revision and idempotency receipt atomically.
        var statusBefore = await Call(s => s.Company.GetCompany(cid, owner, default));
        var statusKey = Fixture.Key();
        int statusAuditBefore;
        await using (var db = Context())
        {
            statusAuditBefore = await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_CompanyStatusAudit] CHECK ([Action] <> N'company.status-changed')");
        }
        try
        {
            await Error("database_constraint", () => Call(s => s.Company.SetCompanyStatus(
                cid, owner, statusKey, new(statusBefore.Version, false, "اختبار فشل تدقيق"), default)));
            await using var db = Context();
            var persisted = await db.Set<Company>().SingleAsync(x => x.Id == cid);
            Ensure(persisted.IsActive && persisted.Version == statusBefore.Version &&
                persisted.Revision == statusBefore.Revision &&
                await db.Set<AuditEvent>().CountAsync(x => x.CompanyId == cid) == statusAuditBefore &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == statusKey),
                "SYS01-OP02 failed status change leaked SQL writes");
        }
        finally
        {
            await using var db = Context();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_CompanyStatusAudit]");
        }
        var disabledAfterRetry = await Call(s => s.Company.SetCompanyStatus(
            cid, owner, statusKey, new(statusBefore.Version, false, "اختبار فشل تدقيق"), default));
        Ensure(!(await Call(s => s.Company.GetCompany(cid, owner, default))).IsActive,
            "SYS01-OP02 identical retry did not persist disabled company");
        await Call(s => s.Company.SetCompanyStatus(
            cid, owner, Fixture.Key(), new(disabledAfterRetry.Version, true, "تفعيل"), default));
        Console.WriteLine("  SQL PASS SYS01-OP02 status rollback on audit failure and exact retry");
        Console.WriteLine("  SQL PASS SYS01 incomplete-profile guard, correction, inactive-company replay denial and reactivation");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
