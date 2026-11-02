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
            Ensure(role.Permits("gl03", "post", 100.123456m, currency) &&
                role.Permits(" GL03 ", " post ", 100.123456m, currency),
                "Allowed capped operation missing for normalized input");
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

        test("SYS07: atomic role rule replacement, revision conflicts and operation isolation", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            var role = new SecurityRole(f.CompanyId, f.BranchId, "Accountant",
                "Accounting", f.Owner.Subject, now);
            role.AddRule("GL03", "POST", true, 50m, currency, now);
            var originalRule = role.Rules.Single().Id;
            var version = role.Revision;
            var modified = role.LastModifiedAt;
            var invalid = new[] {
                new SecurityRoleRuleDraft("GL03", "POST", true, 100m, currency),
                new SecurityRoleRuleDraft("GL03", "EXPORT", false, 1m, currency)
            };
            Expect("invalid_role_amount_policy", () =>
                role.ReplaceRules(invalid, version, now.AddSeconds(1)));
            Ensure(role.Revision == version && role.LastModifiedAt == modified &&
                role.Rules.Single().Id == originalRule, "Invalid batch changed persisted aggregate");
            var duplicate = new[] {
                new SecurityRoleRuleDraft("gl03", "post", true, null, null),
                new SecurityRoleRuleDraft("GL03", "POST", false, null, null)
            };
            Expect("duplicate_role_rule", () =>
                role.ReplaceRules(duplicate, version, now.AddSeconds(1)));
            var withNull = new SecurityRoleRuleDraft[] {
                new("GL03", "EXPORT", true, null, null), null!
            };
            Expect("invalid_role_rules", () =>
                role.ReplaceRules(withNull, version, now.AddSeconds(1)));
            Expect("stale_role_revision", () =>
                role.ReplaceRules(Array.Empty<SecurityRoleRuleDraft>(), version - 1, now.AddSeconds(1)));
            Ensure(role.Revision == version && role.Rules.Single().Id == originalRule,
                "Rejected revision or duplicate changed rules");
            role.ReplaceRules(new[] {
                new SecurityRoleRuleDraft("GL03", "POST", true, 100m, currency),
                new SecurityRoleRuleDraft("GL03", "EXPORT", false, null, null),
                new SecurityRoleRuleDraft("GL03", "COST", true, null, null)
            }, version, now.AddSeconds(1));
            Ensure(role.Revision == version + 1 && role.Rules.Count == 3 &&
                role.Permits("GL03", "POST", 100m, currency) &&
                !role.Permits("GL03", "POST", 101m, currency) &&
                !role.Permits("GL03", "EXPORT", null, null) &&
                role.Permits("GL03", "COST", null, null),
                "Atomic replacement or independent operations failed");
            Expect("stale_role_revision", () =>
                role.SetActive(false, version, now.AddSeconds(2)));
            Expect("stale_role_revision", () =>
                role.Edit("Changed", "Accounting", version, now.AddSeconds(2)));
            Ensure(role.IsActive && role.Name == "Accountant" &&
                role.Revision == version + 1, "Stale update changed role");
            role.SetActive(false, version + 1, now.AddSeconds(2));
            Ensure(!role.IsActive && role.Revision == version + 2,
                "Current revision must allow deactivation");
            return Task.CompletedTask;
        });

        test("SYS07/SYS08: assigned roles enforce tenant, branch, explicit deny and caps", f =>
        {
            var now = DateTimeOffset.UtcNow;
            var member = new Membership(f.CompanyId, f.Owner.Subject,
                new[] { Permissions.Read }, false, new[] { f.BranchId });
            var currency = Guid.NewGuid();
            var grant = new SecurityRole(f.CompanyId, f.BranchId, "Poster", "Can post",
                f.Owner.Subject, now);
            grant.AddRule("GL03", "POST", true, 100m, currency, now);
            var denial = new SecurityRole(f.CompanyId, null, "Deny", "No post",
                f.Owner.Subject, now);
            denial.AddRule("GL03", "POST", false, null, null, now);
            var assignments = new[] {
                new SecurityRoleAssignment(f.CompanyId, member.Id, grant.Id),
                new SecurityRoleAssignment(f.CompanyId, member.Id, denial.Id)
            };
            bool Allowed(IEnumerable<SecurityRoleAssignment> links, IEnumerable<SecurityRole> roles,
                Guid company, Guid branch, decimal? amount, Guid? money) =>
                SecurityRolePolicyEvaluator.Permits(member, links, roles, company, branch,
                    "GL03", "POST", amount, money);
            Ensure(!Allowed(assignments, new[] { grant, denial }, f.CompanyId, f.BranchId, 1m, currency),
                "Explicit denial across assigned roles must win");
            Ensure(Allowed(assignments.Take(1), new[] { grant, denial }, f.CompanyId, f.BranchId, 100m, currency),
                "Matching assigned grant should allow its exact cap");
            Ensure(!Allowed(assignments.Take(1), new[] { grant }, f.CompanyId, f.BranchId, 101m, currency) &&
                !Allowed(assignments.Take(1), new[] { grant }, f.CompanyId, f.BranchId, 1m, Guid.NewGuid()) &&
                !Allowed(assignments.Take(1), new[] { grant }, f.CompanyId, Guid.NewGuid(), 1m, currency) &&
                !Allowed(assignments.Take(1), new[] { grant }, Guid.NewGuid(), f.BranchId, 1m, currency) &&
                !Allowed(Array.Empty<SecurityRoleAssignment>(), new[] { grant }, f.CompanyId, f.BranchId, 1m, currency),
                "Cap, currency, membership scope, tenant or assignment bypass");
            var outsider = new Membership(Guid.NewGuid(), "other", new[] { Permissions.Read }, true, Array.Empty<Guid>());
            Ensure(!SecurityRolePolicyEvaluator.Permits(outsider, assignments, new[] { grant },
                f.CompanyId, f.BranchId, "GL03", "POST", 1m, currency),
                "Foreign membership must not grant access");
            // An omitted assigned denial must never be treated as a valid grant.
            Ensure(!Allowed(assignments, new[] { grant }, f.CompanyId, f.BranchId, 1m, currency),
                "Incomplete role snapshot bypassed assigned denial");
            Ensure(!Allowed(assignments, new[] { grant, grant }, f.CompanyId, f.BranchId, 1m, currency),
                "Duplicate role snapshot masked a missing assigned denial");
            Ensure(!Allowed(new[] {
                assignments[0],
                new SecurityRoleAssignment(Guid.NewGuid(), member.Id, denial.Id)
            }, new[] { grant, denial }, f.CompanyId, f.BranchId, 1m, currency),
                "Foreign-company assignment must fail closed");
            Ensure(!Allowed(new[] { assignments[0], assignments[0] },
                new[] { grant }, f.CompanyId, f.BranchId, 1m, currency),
                "Duplicate assignment must fail closed");
            Ensure(Allowed(assignments.Take(1), new[] { grant, denial },
                f.CompanyId, f.BranchId, 1m, currency),
                "Unassigned roles must not affect a complete role snapshot");
            Ensure(SecurityRolePolicyEvaluator.Permits(member, assignments.Take(1),
                new[] { grant }, f.CompanyId, f.BranchId, " GL03 ", " post ", 1m, currency),
                "Whitespace-normalized action lookup must remain valid");
            member.SetActive(false);
            Ensure(!Allowed(assignments.Take(1), new[] { grant }, f.CompanyId, f.BranchId, 1m, currency),
                "Inactive membership must not grant access");
            return Task.CompletedTask;
        });

        test("SYS07: malformed role rule snapshots fail closed for direct and assigned checks", f =>
        {
            var now = DateTimeOffset.UtcNow;
            var member = new Membership(f.CompanyId, f.Owner.Subject,
                new[] { Permissions.Read }, false, new[] { f.BranchId });
            var role = new SecurityRole(f.CompanyId, f.BranchId, "Poster", "Post journals",
                f.Owner.Subject, now);
            role.AddRule("GL03", "POST", true, null, null, now);
            var link = new SecurityRoleAssignment(f.CompanyId, member.Id, role.Id);
            bool Assigned() => SecurityRolePolicyEvaluator.Permits(member,
                new[] { link }, new[] { role }, f.CompanyId, f.BranchId,
                "GL03", "POST", null, null);
            Ensure(role.Permits("GL03", "POST", null, null) && Assigned(),
                "Valid role snapshot must retain its explicit grant");
            var foreign = new SecurityRole(Guid.NewGuid(), null, "Foreign", "Other company",
                f.Owner.Subject, now);
            foreign.AddRule("GL03", "EXPORT", true, null, null, now);
            role.Rules.Add(foreign.Rules.Single());
            Ensure(!role.Permits("GL03", "POST", null, null) && !Assigned(),
                "Foreign-company rule in role snapshot must fail closed");
            role.Rules.RemoveAt(1);
            role.Rules.Add(role.Rules[0]);
            Ensure(!role.Permits("GL03", "POST", null, null) && !Assigned(),
                "Duplicate role rule in snapshot must fail closed");
            role.Rules.RemoveAt(1);
            Ensure(role.Permits("GL03", "POST", null, null) && Assigned(),
                "Restored valid snapshot must preserve grant");
            return Task.CompletedTask;
        });

        test("SYS07: hydrated rule invariants and null snapshot rows fail closed", f =>
        {
            var now = DateTimeOffset.UtcNow;
            var member = new Membership(f.CompanyId, f.Owner.Subject,
                new[] { Permissions.Read }, false, new[] { f.BranchId });
            var role = new SecurityRole(f.CompanyId, f.BranchId, "Poster", "Post journals",
                f.Owner.Subject, now);
            role.AddRule("GL03", "POST", true, null, null, now);
            var rule = role.Rules.Single();
            var link = new SecurityRoleAssignment(f.CompanyId, member.Id, role.Id);
            bool Direct() => role.Permits("GL03", "POST", null, null);
            bool Assigned(IEnumerable<SecurityRoleAssignment> links, IEnumerable<SecurityRole> roles) =>
                SecurityRolePolicyEvaluator.Permits(member, links, roles, f.CompanyId,
                    f.BranchId, "GL03", "POST", null, null);
            Ensure(Direct() && Assigned(new[] { link }, new[] { role }),
                "Valid snapshot must retain its grant");
            Ensure(!Assigned(new SecurityRoleAssignment[] { link, null! }, new[] { role }) &&
                !Assigned(new[] { link }, new SecurityRole[] { role, null! }),
                "Null snapshot rows must never grant access");
            void Corrupt(string property, object? badValue, object? originalValue)
            {
                var propertyInfo = typeof(SecurityRoleRule).GetProperty(property)!;
                propertyInfo.SetValue(rule, badValue);
                Ensure(!Direct() && !Assigned(new[] { link }, new[] { role }),
                    $"Corrupt hydrated {property} must fail closed");
                propertyInfo.SetValue(rule, originalValue);
                Ensure(Direct() && Assigned(new[] { link }, new[] { role }),
                    $"Restored {property} must retain valid grant");
            }
            Corrupt(nameof(SecurityRoleRule.ScreenCode), "gl03", "GL03");
            Corrupt(nameof(SecurityRoleRule.OperationCode), "POST ", "POST");
            Corrupt(nameof(SecurityRoleRule.AmountLimit), 1m, null);
            Corrupt(nameof(SecurityRoleRule.CurrencyId), Guid.NewGuid(), null);
            Corrupt(nameof(SecurityRoleRule.RoleId), Guid.Empty, role.Id);
            return Task.CompletedTask;
        });

        test("SYS07 security: deny-first prevents cross-role privilege escalation across independent operations", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
            var member = new Membership(f.CompanyId, f.Owner.Subject,
                new[] { Permissions.Read }, false, new[] { f.BranchId });
            var currency = Guid.NewGuid();
            var permitted = new SecurityRole(f.CompanyId, f.BranchId, "Operator", "Separate actions", f.Owner.Subject, now);
            permitted.AddRule("GL03", "POST", true, 25m, currency, now);
            permitted.AddRule("GL03", "COST", true, null, null, now);
            permitted.AddRule("GL03", "EXPORT", true, null, null, now);
            var denied = new SecurityRole(f.CompanyId, null, "Restricted", "No export", f.Owner.Subject, now);
            denied.AddRule("GL03", "EXPORT", false, null, null, now);
            var links = new[] {
                new SecurityRoleAssignment(f.CompanyId, member.Id, permitted.Id),
                new SecurityRoleAssignment(f.CompanyId, member.Id, denied.Id)
            };
            bool Allows(string action, decimal? amount = null, Guid? money = null) =>
                SecurityRolePolicyEvaluator.Permits(member, links, new[] { permitted, denied },
                    f.CompanyId, f.BranchId, "GL03", action, amount, money);
            Ensure(Allows("POST", 25m, currency) && Allows("COST"),
                "Independent approved operations must remain permitted");
            Ensure(!Allows("EXPORT") && !Allows("POST", 25.000001m, currency) &&
                !Allows("POST", 1m, Guid.NewGuid()) && !Allows("POST", null, currency) &&
                !Allows("APPROVE"), "Deny, cap, currency or missing permission escalated privileges");
            denied.SetActive(false, now.AddSeconds(1));
            Ensure(Allows("EXPORT"), "Disabled deny role unexpectedly blocked independent permission");
            denied.SetActive(true, now.AddSeconds(2));
            Ensure(!Allows("EXPORT"), "Reactivated explicit deny failed to take precedence");
            return Task.CompletedTask;
        });

        test("SYS07/SYS08: assignment FK model and EF guard reject unverified writes", async f =>
        {
            Expect("invalid_role_assignment", () => new SecurityRoleAssignment(f.CompanyId,
                Guid.Empty, Guid.NewGuid()));
            await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
            var entity = db.Model.FindEntityType(typeof(SecurityRoleAssignment))!;
            Ensure(entity.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(Membership) &&
                    k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "CompanyId", "MembershipId" })) &&
                entity.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(SecurityRole) &&
                    k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "CompanyId", "RoleId" })) &&
                entity.GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 3),
                "Role assignment tenant FKs or uniqueness missing");
            db.Add(new SecurityRoleAssignment(f.CompanyId, Guid.NewGuid(), Guid.NewGuid()));
            try { await db.SaveChangesAsync(); throw new Exception("Unverified assignment persisted"); }
            catch (BusinessException ex) when (ex.Code == "security_role_assignment_policy_unavailable") { }
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
