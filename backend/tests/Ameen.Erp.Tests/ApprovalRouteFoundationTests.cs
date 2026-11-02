using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class ApprovalRouteFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("SYS09 foundation: amount, stage, branch, currency and audit fields retain exact types", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            var role = Guid.NewGuid();
            var route = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 12.123456m,
                200.000001m, currency, true, f.Owner.Subject, now);
            Ensure(route.CompanyId == f.CompanyId && route.BranchId == f.BranchId &&
                route.DocumentType == "JOURNAL" && route.MinimumAmount == 12.123456m &&
                route.MaximumAmount == 200.000001m && route.CurrencyId == currency &&
                route.SeparateCreatorAndApprover && route.IsActive && route.Revision == 1 &&
                route.CreatedAt == now && route.LastModifiedAt == now, "SYS09 header mapping differs");
            var stage = route.AddStage(1, role, 60, now.AddSeconds(1));
            Ensure(stage.CompanyId == f.CompanyId && stage.ApprovalRouteId == route.Id &&
                stage.StageNumber == 1 && stage.ApproverRoleId == role && stage.AlertAfterMinutes == 60 &&
                route.Revision == 2, "SYS09 stage mapping differs");
            var previousVersion = route.Version;
            route.Edit("journal", 0, null, currency, false, now.AddMinutes(1));
            Ensure(route.MinimumAmount == 0 && route.MaximumAmount is null &&
                !route.SeparateCreatorAndApprover && route.Revision == 3 &&
                route.Version != previousVersion, "SYS09 revision/edit differs");
            route.SetActive(false, now.AddMinutes(2));
            Ensure(!route.IsActive && route.Revision == 4, "SYS09 status revision differs");
            return Task.CompletedTask;
        });

        test("SYS09 foundation: invalid bounds, duplicate stages, role and stale revisions fail closed", f =>
        {
            var now = DateTimeOffset.UtcNow;
            var currency = Guid.NewGuid();
            Expect("invalid_approval_amount_range", () => new ApprovalRoute(f.CompanyId, null,
                "journal", 1.1234567m, null, currency, true, "actor", now));
            Expect("invalid_approval_amount_range", () => new ApprovalRoute(f.CompanyId, null,
                "journal", 200, 100, currency, true, "actor", now));
            Expect("approval_currency_required", () => new ApprovalRoute(f.CompanyId, null,
                "journal", 0, null, Guid.Empty, true, "actor", now));
            Expect("invalid_approval_branch", () => new ApprovalRoute(f.CompanyId, Guid.Empty,
                "journal", 0, null, currency, true, "actor", now));
            var route = new ApprovalRoute(f.CompanyId, null, "journal", 0, null, currency, true, "actor", now);
            Expect("invalid_approval_role_reference", () => route.AddStage(1, Guid.Empty, null, now));
            Expect("invalid_approval_stage", () => route.AddStage(0, Guid.NewGuid(), null, now));
            Expect("invalid_approval_stage", () => route.AddStage(1, Guid.NewGuid(), -1, now));
            route.AddStage(1, Guid.NewGuid(), 0, now);
            Expect("duplicate_approval_stage", () => route.AddStage(1, Guid.NewGuid(), null, now));
            Expect("invalid_approval_revision", () => route.SetActive(false, now.AddSeconds(-1)));
            Ensure(route.IsActive && route.Stages.Count == 1 && route.Revision == 2,
                "Rejected SYS09 command changed route");
            return Task.CompletedTask;
        });

        test("SYS09 foundation: tenant FKs, stage uniqueness, GL14 route FK and guarded EF writes", async f =>
        {
            await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
            var route = db.Model.FindEntityType(typeof(ApprovalRoute))!;
            var stage = db.Model.FindEntityType(typeof(ApprovalRouteStage))!;
            var type = db.Model.FindEntityType(typeof(JournalDocumentType))!;
            Ensure(route.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(Currency) &&
                    k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "CompanyId", "CurrencyId" })) &&
                route.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(Branch)) &&
                stage.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(ApprovalRoute) &&
                    k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "ApprovalRouteId", "CompanyId" })) &&
                stage.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "CompanyId", "ApprovalRouteId", "StageNumber" })) &&
                type.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(ApprovalRoute)),
                "SYS09/GL14 tenant constraints missing");
            var card = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 0, null,
                Guid.NewGuid(), true, f.Owner.Subject, DateTimeOffset.UtcNow);
            card.AddStage(1, Guid.NewGuid(), 30, DateTimeOffset.UtcNow.AddSeconds(1));
            db.Add(card);
            try { await db.SaveChangesAsync(); throw new Exception("Unvalidated SYS09 role was persisted"); }
            catch (BusinessException ex) when (ex.Code == "approval_route_role_policy_unavailable") { }
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
