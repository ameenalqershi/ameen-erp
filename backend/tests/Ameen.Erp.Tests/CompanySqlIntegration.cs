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
        async Task<T> Call<T>(Func<ErpService, Task<T>> work)
        {
            await using var db = Context(); return await work(new ErpService(new SqlErpStore(db), new SqlTransactionRunner(db, new FixedClock()), new FixedClock()));
        }
        var company = await Call(s => s.GetCompany(cid, owner, default));
        var request = CompanyTests.Edit(company) with { Profile = CompanyTests.Profile(company) with { City = "مدينة SQL", CommercialRegistration = "0000123", Notes = "دليل" } };
        var key = Fixture.Key();
        var edits = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(s => s.EditCompany(cid, owner, key, request, default))));
        Ensure(edits.Distinct().Count() == 1, "Company concurrent replay differs");
        company = await Call(s => s.GetCompany(cid, owner, default));
        Ensure(company.Revision == 2 && company.CommercialRegistration == "0000123" && company.City == "مدينة SQL" && company.IsProfileComplete, "Company profile SQL differs");
        Ensure((await Call(s => s.ListCompanyAudit(cid, owner, 0, 20, default))).Items.Count(x => x.Action == "company.edited") == 1, "Company replay duplicated audit");
        Console.WriteLine("  SQL PASS SYS01 profile, nullable date, leading zeros and concurrent edit replay");

        var failureRequest = CompanyTests.Edit(company) with { Name = "فشل ثم إعادة" }; var failureKey = Fixture.Key();
        await using (var db = Context()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] WITH NOCHECK ADD CONSTRAINT [CK_Test_CompanyAudit] CHECK ([Action] <> N'company.edited')");
        try
        {
            await Error("database_constraint", () => Call(s => s.EditCompany(cid, owner, failureKey, failureRequest, default)));
            await using var db = Context();
            var saved = await db.Set<Company>().SingleAsync(x => x.Id == cid);
            Ensure(saved.Version == company.Version && saved.Revision == company.Revision && saved.Name == company.Name &&
                !await db.Set<IdempotencyRecord>().AnyAsync(x => x.CompanyId == cid && x.Key == failureKey), "Failed company edit leaked changes");
        }
        finally { await using var db = Context(); await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AuditEvents] DROP CONSTRAINT [CK_Test_CompanyAudit]"); }
        await Call(s => s.EditCompany(cid, owner, failureKey, failureRequest, default));
        Console.WriteLine("  SQL PASS SYS01 audit failure rolls back company revision, fields and receipt; exact retry succeeds");

        company = await Call(s => s.GetCompany(cid, owner, default)); var completeProfile = CompanyTests.Profile(company);
        await using (var db = Context()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Companies] SET [LegalName]=NULL WHERE [Id]={cid}");
        var incomplete = await Call(s => s.GetCompany(cid, owner, default));
        Ensure(!incomplete.IsProfileComplete, "Legacy incomplete profile not detected");
        await Error("company_profile_incomplete", () => Call(s => s.CreateBranch(cid, owner, Fixture.Key(), new("PROFILE-BLOCKED", "محجوب"), default)));
        await Call(s => s.EditCompany(cid, owner, Fixture.Key(), new(incomplete.Version, incomplete.Code, incomplete.Name, incomplete.CountryCode,
            incomplete.FunctionalCurrency, completeProfile, "استكمال بيانات قديمة"), default));
        var branchKey = Fixture.Key(); var branchRequest = new CreateBranchRequest("COMPANY-STATUS", "اختبار الحالة");
        var branch = await Call(s => s.CreateBranch(cid, owner, branchKey, branchRequest, default));
        company = await Call(s => s.GetCompany(cid, owner, default));
        var off = await Call(s => s.SetCompanyStatus(cid, owner, Fixture.Key(), new(company.Version, false, "تعطيل"), default));
        await Error("company_inactive", () => Call(s => s.CreateBranch(cid, owner, branchKey, branchRequest, default)));
        Ensure(!(await Call(s => s.GetCompany(cid, owner, default))).IsActive, "Inactive company read failed");
        await Call(s => s.SetCompanyStatus(cid, owner, Fixture.Key(), new(off.Version, true, "تفعيل"), default));
        Ensure(await Call(s => s.CreateBranch(cid, owner, branchKey, branchRequest, default)) == branch, "Reactivated command replay differs");
        Console.WriteLine("  SQL PASS SYS01 incomplete-profile guard, correction, inactive-company replay denial and reactivation");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Error(string code, Func<Task> action)
    {
        try { await action(); } catch (BusinessException e) when (e.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
