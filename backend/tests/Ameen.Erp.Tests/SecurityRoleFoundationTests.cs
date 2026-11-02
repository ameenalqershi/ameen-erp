using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class SecurityRoleFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS07 foundation: role header, rule dimensions, explicit deny and amount cap", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 6, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            var role = new SecurityRole(f.CompanyId, f.BranchId, "Accountant", "Accounting operations",
                f.Owner.Subject, now);
            Ensure(role.CompanyId == f.CompanyId && role.BranchId == f.BranchId &&
                role.IsActive && role.Revision == 1 && role.CreatedAt == now, "SYS07 role header mismatch");
            role.AddRule("GL03", "POST", true, 100.123456m, currency, now.AddSeconds(1));
            Ensure(role.Permits("gl03", "post", 100.123456m, currency), "Allowed capped operation missing");
            Ensure(!role.Permits("GL03", "POST", 101m, currency) &&
                !role.Permits("GL03", "POST", null, currency) &&
                !role.Permits("GL03", "POST", 1m, Guid.NewGuid()) &&
                !role.Permits("GL03", "EXPORT", 1m, currency), "Cap/currency/operation not isolated");
            role.AddRule("GL03", "POST", false, null, null, now.AddSeconds(2));
            Ensure(!role.Permits("GL03", "POST", 1m, currency), "Explicit deny must win");
            role.SetActive(false, now.AddSeconds(3));
            Ensure(!role.IsActive && role.Revision == 4, "Role deactivation revision mismatch");
            return Task.CompletedTask;
        });

        test("SYS07 foundation: invalid policy, duplicate rule and stale revision are atomic", f =>
        {
            var now = DateTimeOffset.UtcNow;
            Expect("invalid_role_branch", () => new SecurityRole(f.CompanyId, Guid.Empty,
                "R", "Responsibility", "actor", now));
            var role = new SecurityRole(f.CompanyId, null, "R", "Responsibility", "actor", now);
            var currency = Guid.NewGuid();
            Expect("invalid_role_amount_policy", () => role.AddRule("GL03", "POST", true, 1m, null, now));
            Expect("invalid_role_amount_policy", () => role.AddRule("GL03", "POST", false, 1m, currency, now));
            Expect("invalid_role_amount_policy", () => role.AddRule("GL03", "POST", true, 1.0000001m, currency, now));
            Expect("invalid_role_amount_policy", () => role.AddRule("GL03", "POST", true, null, currency, now));
            role.AddRule("GL03", "POST", true, 5m, currency, now);
            Expect("duplicate_role_rule", () => role.AddRule("GL03", "POST", true, 6m, currency, now.AddSeconds(1)));
            Expect("invalid_role_revision", () => role.Edit("Changed", "Responsibility", now.AddSeconds(-1)));
            Ensure(role.Name == "R" && role.Rules.Count == 1 && role.Revision == 2,
                "Rejected SYS07 operation changed role");
            return Task.CompletedTask;
        });

        test("SYS07 foundation: tenant role, currency and SYS09 approver FKs; writes fail closed", async f =>
        {
            await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
            var roleType = db.Model.FindEntityType(typeof(SecurityRole))!;
            var ruleType = db.Model.FindEntityType(typeof(SecurityRoleRule))!;
            var stageType = db.Model.FindEntityType(typeof(ApprovalRouteStage))!;
            Ensure(roleType.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(Branch) &&
                    k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "BranchId", "CompanyId" })) &&
                ruleType.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(SecurityRole)) &&
                ruleType.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(Currency)) &&
                ruleType.GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 5) &&
                stageType.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(SecurityRole) &&
                    k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "ApproverRoleId", "CompanyId" })),
                "SYS07/SYS09 tenant FK or rule uniqueness missing");
            var role = new SecurityRole(f.CompanyId, f.BranchId, "R", "Responsibility",
                f.Owner.Subject, DateTimeOffset.UtcNow);
            role.AddRule("GL03", "POST", true, null, null, DateTimeOffset.UtcNow.AddSeconds(1));
            db.Add(role);
            try { await db.SaveChangesAsync(); throw new Exception("Unverified SYS07 role was persisted"); }
            catch (BusinessException ex) when (ex.Code == "security_role_policy_unavailable") { }
        });
    }

    private static void Ensure(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Expect(string code, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
