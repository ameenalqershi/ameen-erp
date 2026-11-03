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

        test("SYS09: pure route overlap preflight respects inclusive amounts and branch scope", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            ApprovalRoute Route(Guid? branch, string kind, decimal min, decimal? max,
                Guid? money = null) => new(f.CompanyId, branch, kind, min, max,
                    money ?? currency, true, f.Owner.Subject, now);
            var candidate = Route(f.BranchId, "journal", 100m, 200m);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { candidate }) ==
                ApprovalRouteConflictStatus.Clear, "Editing the same route is not a collision");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(f.BranchId, "journal", 200m, 300m) }) ==
                ApprovalRouteConflictStatus.Overlap, "Inclusive endpoint collision not detected");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(f.BranchId, "journal", 201m, 300m) }) ==
                ApprovalRouteConflictStatus.Clear, "Disjoint same-currency interval rejected");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(null, "journal", 0m, null) }) ==
                ApprovalRouteConflictStatus.Overlap, "Company-wide route must intersect branch route");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(Guid.NewGuid(), "journal", 100m, 200m) }) ==
                ApprovalRouteConflictStatus.Clear, "Distinct explicit branches should not intersect");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(f.BranchId, "other", 100m, 200m) }) ==
                ApprovalRouteConflictStatus.Clear, "Distinct document kinds should not intersect");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(f.BranchId, "journal", 999m, 1000m, Guid.NewGuid()) }) ==
                ApprovalRouteConflictStatus.UnresolvedCurrency,
                "Different currencies must not be compared by raw amount");
            var inactive = Route(null, "journal", 0m, null);
            inactive.SetActive(false, now.AddSeconds(1));
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { inactive }) ==
                ApprovalRouteConflictStatus.Clear, "Inactive routes cannot conflict");
            return Task.CompletedTask;
        });

        test("SYS09: inactive candidate releases interval but never bypasses snapshot validation", f =>
        {
            var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            ApprovalRoute Route(Guid company, Guid money) => new(company, f.BranchId,
                "journal", 0m, 100m, money, true, f.Owner.Subject, now);
            var candidate = Route(f.CompanyId, currency);
            var existing = Route(f.CompanyId, currency);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.Overlap, "Active candidate overlap not detected");
            candidate.SetActive(false, now.AddSeconds(1));
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.Clear, "Disabled candidate reserved an interval");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(f.CompanyId, Guid.NewGuid()) }) == ApprovalRouteConflictStatus.Clear,
                "Disabled candidate triggered an unresolved FX comparison");
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] {
                Route(Guid.NewGuid(), currency) }) == ApprovalRouteConflictStatus.InvalidSnapshot &&
                ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing, existing }) ==
                    ApprovalRouteConflictStatus.InvalidSnapshot,
                "Disabled candidate bypassed tenant or duplicate snapshot validation");
            candidate.SetActive(true, now.AddSeconds(2));
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.Overlap, "Reactivated candidate escaped conflict check");
            return Task.CompletedTask;
        });

        test("SYS09: route preflight rejects incomplete tenant snapshots", f =>
        {
            var now = DateTimeOffset.UtcNow;
            var currency = Guid.NewGuid();
            var candidate = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 0m,
                null, currency, true, f.Owner.Subject, now);
            var other = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 10m,
                20m, currency, true, f.Owner.Subject, now);
            var foreign = new ApprovalRoute(Guid.NewGuid(), f.BranchId, "journal", 0m,
                null, currency, true, f.Owner.Subject, now);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, null) ==
                ApprovalRouteConflictStatus.InvalidSnapshot &&
                ApprovalRouteConflictPolicy.Evaluate(null, new[] { other }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot &&
                ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { other, other }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot &&
                ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { other, foreign }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot &&
                ApprovalRouteConflictPolicy.Evaluate(candidate, new ApprovalRoute[] { other, null! }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot,
                "Incomplete, duplicate, foreign or null snapshot was accepted");
            return Task.CompletedTask;
        });

        test("SYS09: hydrated stage corruption invalidates conflict preflight", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            ApprovalRoute Route() => new(f.CompanyId, f.BranchId, "journal", 10m,
                20m, currency, true, f.Owner.Subject, now);
            var candidate = Route();
            var existing = Route();
            var stage = existing.AddStage(1, Guid.NewGuid(), 30, now);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.Overlap, "Valid staged overlap must be detected");
            existing.Stages.Add(stage);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot, "Duplicate stage was accepted");
            existing.Stages.RemoveAt(1);
            var foreign = Route();
            foreign.AddStage(2, Guid.NewGuid(), null, now);
            existing.Stages.Add(foreign.Stages.Single());
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot, "Foreign route stage was accepted");
            existing.Stages.RemoveAt(1);
            existing.Stages.Add(null!);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot, "Null stage was accepted");
            existing.Stages.RemoveAt(1);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.Overlap, "Restored stage snapshot must detect overlap");
            return Task.CompletedTask;
        });

        test("SYS09: duplicate stage identities across distinct routes fail closed", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            ApprovalRoute Route() => new(f.CompanyId, f.BranchId, "journal", 10m,
                20m, currency, true, f.Owner.Subject, now);
            var candidate = Route();
            var first = Route();
            var second = Route();
            var candidateStage = candidate.AddStage(1, Guid.NewGuid(), null, now);
            var firstStage = first.AddStage(1, Guid.NewGuid(), null, now);
            var secondStage = second.AddStage(1, Guid.NewGuid(), null, now);
            var original = secondStage.Id;
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { first, second }) ==
                ApprovalRouteConflictStatus.Overlap, "Valid stage identities must allow overlap detection");
            // EF hydration corruption: two distinct routes claim the same stage PK.
            typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(secondStage, firstStage.Id);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { first, second }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot, "Cross-route duplicate stage PK was accepted");
            typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(secondStage, candidateStage.Id);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { first, second }) ==
                ApprovalRouteConflictStatus.InvalidSnapshot, "Candidate/tenant duplicate stage PK was accepted");
            typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(secondStage, original);
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { candidate, first, second }) ==
                ApprovalRouteConflictStatus.Overlap, "Own persisted candidate must not be double counted");
            return Task.CompletedTask;
        });

        test("SYS09: corrupt hydrated route audit invalidates conflict preflight", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            ApprovalRoute Route() => new(f.CompanyId, f.BranchId, "journal", 10m,
                20m, currency, true, f.Owner.Subject, now);
            var candidate = Route();
            var existing = Route();
            Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                ApprovalRouteConflictStatus.Overlap, "Valid route should overlap before corruption");
            void Corrupt(ApprovalRoute target, string property, object value)
            {
                var info = typeof(ApprovalRoute).GetProperty(property)!;
                var original = info.GetValue(target);
                info.SetValue(target, value);
                Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                    ApprovalRouteConflictStatus.InvalidSnapshot,
                    $"Corrupt SYS09 {property} on {(ReferenceEquals(target, candidate) ? "candidate" : "tenant row")} was accepted");
                info.SetValue(target, original);
                Ensure(ApprovalRouteConflictPolicy.Evaluate(candidate, new[] { existing }) ==
                    ApprovalRouteConflictStatus.Overlap, "Restored audit must allow valid overlap");
            }
            Corrupt(existing, nameof(ApprovalRoute.Revision), 0);
            Corrupt(existing, nameof(ApprovalRoute.CreatedBy), "  ");
            Corrupt(existing, nameof(ApprovalRoute.CreatedBy), new string('X', 201));
            Corrupt(existing, nameof(ApprovalRoute.CreatedBy), " actor ");
            Corrupt(existing, nameof(ApprovalRoute.CreatedAt), default(DateTimeOffset));
            Corrupt(existing, nameof(ApprovalRoute.LastModifiedAt), now.AddSeconds(-1));
            Corrupt(candidate, nameof(ApprovalRoute.Revision), -1);
            Corrupt(candidate, nameof(ApprovalRoute.LastModifiedAt), now.AddSeconds(-1));
            return Task.CompletedTask;
        });

        test("SYS09: atomic stage replacement and expected revision fail closed", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            var role = Guid.NewGuid();
            var route = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 0m,
                null, currency, true, f.Owner.Subject, now);
            route.AddStage(1, role, 30, now);
            var revision = route.Revision;
            var original = route.Stages.Single().Id;
            var invalid = new[] {
                new ApprovalRouteStageDraft(2, Guid.NewGuid(), 10),
                new ApprovalRouteStageDraft(3, Guid.Empty, 20)
            };
            Expect("invalid_approval_role_reference", () =>
                route.ReplaceStages(invalid, revision, now.AddSeconds(1)));
            Expect("duplicate_approval_stage", () => route.ReplaceStages(new[] {
                new ApprovalRouteStageDraft(1, role, 30),
                new ApprovalRouteStageDraft(1, Guid.NewGuid(), 60)
            }, revision, now.AddSeconds(1)));
            Expect("invalid_approval_stages", () => route.ReplaceStages(new ApprovalRouteStageDraft[] {
                new(2, role, null), null!
            }, revision, now.AddSeconds(1)));
            Expect("stale_approval_revision", () => route.ReplaceStages(Array.Empty<ApprovalRouteStageDraft>(),
                revision - 1, now.AddSeconds(1)));
            Ensure(route.Revision == revision && route.Stages.Single().Id == original,
                "Rejected stage batch changed route");
            route.ReplaceStages(new[] {
                new ApprovalRouteStageDraft(2, role, null),
                new ApprovalRouteStageDraft(4, Guid.NewGuid(), 45)
            }, revision, now.AddSeconds(1));
            Ensure(route.Revision == revision + 1 && route.Stages.Count == 2 &&
                route.Stages.Select(x => x.StageNumber).SequenceEqual(new[] { 2, 4 }),
                "Atomic replacement did not commit once");
            Expect("stale_approval_revision", () => route.Edit("journal", 0m, null,
                currency, true, revision, now.AddSeconds(2)));
            Expect("stale_approval_revision", () => route.SetActive(false, revision, now.AddSeconds(2)));
            Ensure(route.IsActive && route.Revision == revision + 1,
                "Stale edit or status command changed route");
            route.SetActive(false, revision + 1, now.AddSeconds(2));
            Ensure(!route.IsActive && route.Revision == revision + 2,
                "Current revision did not allow status update");
            return Task.CompletedTask;
        });

        test("SYS09 stage insertion: rejected stale revision never attaches stage", f =>
        {
            var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
            var route = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 0, null,
                Guid.NewGuid(), true, "actor", now);
            var originalVersion = route.Version;
            Expect("invalid_approval_revision", () => route.AddStage(1, Guid.NewGuid(), 30, now.AddSeconds(-1)));
            Ensure(route.Stages.Count == 0 && route.Revision == 1 && route.LastModifiedAt == now &&
                route.Version == originalVersion, "Failed stage insertion mutated SYS09 route");
            route.AddStage(1, Guid.NewGuid(), 30, now.AddSeconds(1));
            var stageVersion = route.Version;
            Expect("invalid_approval_revision", () => route.AddStage(2, Guid.NewGuid(), 30, now));
            Ensure(route.Stages.Count == 1 && route.Revision == 2 &&
                route.LastModifiedAt == now.AddSeconds(1) && route.Version == stageVersion,
                "Rejected stale stage insertion changed accepted stages or audit");
            return Task.CompletedTask;
        });

        test("SYS09-FG04: rejected route changes preserve audit, amounts and stage snapshots", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
            var currency = Guid.NewGuid();
            var role = Guid.NewGuid();
            var route = new ApprovalRoute(f.CompanyId, f.BranchId, "journal", 10m, 100m,
                currency, true, f.Owner.Subject, now);
            route.AddStage(1, role, 30, now.AddSeconds(1));
            var revision = route.Revision;
            var version = route.Version;
            var modified = route.LastModifiedAt;
            var stageId = route.Stages.Single().Id;

            void Unchanged()
            {
                Ensure(route.Revision == revision && route.Version == version &&
                    route.LastModifiedAt == modified && route.CreatedAt == now &&
                    route.CreatedBy == f.Owner.Subject && route.IsActive &&
                    route.DocumentType == "JOURNAL" && route.MinimumAmount == 10m &&
                    route.MaximumAmount == 100m && route.CurrencyId == currency &&
                    route.SeparateCreatorAndApprover && route.Stages.Single().Id == stageId,
                    "Rejected SYS09 update mutated audit, header or stages");
            }

            Expect("stale_approval_revision", () => route.Edit("changed", 20m, 200m,
                Guid.NewGuid(), false, revision - 1, now.AddSeconds(2)));
            Unchanged();
            Expect("invalid_approval_amount_range", () => route.Edit("changed", 200m, 100m,
                currency, false, revision, now.AddSeconds(2)));
            Unchanged();
            Expect("approval_currency_required", () => route.Edit("changed", 0m, null,
                Guid.Empty, false, revision, now.AddSeconds(2)));
            Unchanged();
            Expect("invalid_approval_revision", () => route.Edit("changed", 0m, null,
                currency, false, revision, now));
            Unchanged();
            Expect("invalid_approval_revision", () => route.SetActive(false,
                revision, now));
            Unchanged();
            Expect("invalid_approval_revision", () => route.ReplaceStages(
                new[] { new ApprovalRouteStageDraft(2, role, 60) },
                revision, now));
            Unchanged();

            route.Edit("journal", 20m, 200m, currency, false, revision, now.AddSeconds(2));
            Ensure(route.Revision == revision + 1 &&
                route.LastModifiedAt == now.AddSeconds(2) &&
                route.MinimumAmount == 20m && route.MaximumAmount == 200m &&
                !route.SeparateCreatorAndApprover && route.Version != version &&
                route.Stages.Single().Id == stageId,
                "Successful SYS09 update did not advance audit atomically");
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
