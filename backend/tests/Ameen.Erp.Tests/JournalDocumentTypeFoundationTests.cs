using Ameen.Erp.Domain;
using Ameen.Erp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Tests;

public static class JournalDocumentTypeFoundationTests
{
    public static void Register(Action<string, Func<Fixture, Task>> test)
    {
        test("GL14-01: type fields preserve code, source, references and server audit revision", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);
            var series = Guid.NewGuid();
            var route = Guid.NewGuid();
            var type = new JournalDocumentType(f.CompanyId, f.BranchId, "jv-001",
                "نوع قيد يدوي", JournalDocumentOrigin.Manual, series, route, true, f.Owner.Subject, now);
            Ensure(type.Code == "JV-001" && type.Name == "نوع قيد يدوي" &&
                type.Origin == JournalDocumentOrigin.Manual && type.BranchId == f.BranchId &&
                type.NumberSeriesId == series && type.ApprovalRouteId == route &&
                type.RequiresAttachment && type.Revision == 1 && type.CreatedAt == now &&
                type.LastModifiedAt == now && type.IsActive, "GL14 field mapping differs");
            var version = type.Version;
            type.Edit("نوع معدل", series, route, false, now.AddMinutes(1));
            Ensure(type.Revision == 2 && type.Version != version && !type.RequiresAttachment &&
                type.LastModifiedAt == now.AddMinutes(1), "GL14 edit did not advance revision");
            type.SetActive(false, now.AddMinutes(2));
            Ensure(!type.IsActive && type.Revision == 3, "GL14 disable did not advance revision");
            return Task.CompletedTask;
        });
        test("GL14-01: invalid source, reference, branch and stale timestamp rejected", f =>
        {
            var now = DateTimeOffset.UtcNow;
            Expect("invalid_document_type_origin", () => new JournalDocumentType(f.CompanyId, null, "X", "نوع",
                (JournalDocumentOrigin)9, Guid.NewGuid(), Guid.NewGuid(), false, "actor", now));
            Expect("invalid_document_type_reference", () => new JournalDocumentType(f.CompanyId, null, "X", "نوع",
                JournalDocumentOrigin.Manual, Guid.Empty, Guid.NewGuid(), false, "actor", now));
            Expect("invalid_document_type_branch", () => new JournalDocumentType(f.CompanyId, Guid.Empty, "X", "نوع",
                JournalDocumentOrigin.Manual, Guid.NewGuid(), Guid.NewGuid(), false, "actor", now));
            var card = new JournalDocumentType(f.CompanyId, null, "0001", "نوع",
                JournalDocumentOrigin.Automatic, Guid.NewGuid(), Guid.NewGuid(), false, "actor", now);
            Ensure(card.Code == "0001", "Leading zeros were lost");
            Ensure(card.MatchesSource(JournalDocumentOrigin.Automatic) &&
                !card.MatchesSource(JournalDocumentOrigin.Manual),
                "GL14 automatic source was misclassified");
            Expect("invalid_document_type_revision", () => card.SetActive(false, now.AddSeconds(-1)));
            Ensure(card.IsActive && card.Revision == 1, "Invalid timestamp mutated card");
            return Task.CompletedTask;
        });
        test("GL14-02 partial: structural references fail closed and never authorize GL14", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
            var series = new DocumentNumberSeries(f.CompanyId, f.BranchId,
                NumberingDocumentKind.Journal, Guid.NewGuid(), "JV", 1, "actor", now);
            var route = new ApprovalRoute(f.CompanyId, f.BranchId, "JV", 0, 100,
                Guid.NewGuid(), true, "actor", now);
            var card = new JournalDocumentType(f.CompanyId, f.BranchId, "JV-001", "قيد",
                JournalDocumentOrigin.Manual, series.Id, route.Id, true, "actor", now);
            static JournalDocumentTypeReferenceStatus CheckRefs(JournalDocumentType? c,
                DocumentNumberSeries? s, ApprovalRoute? r) =>
                JournalDocumentTypeReferencePolicy.Evaluate(c, s, r);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.UnresolvedPolicy,
                "Structurally matching references must not grant unapproved use");
            Ensure(CheckRefs(null, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                CheckRefs(card, null, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                CheckRefs(card, series, null) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Missing references accepted");
            var foreignSeries = new DocumentNumberSeries(Guid.NewGuid(), f.BranchId,
                NumberingDocumentKind.Journal, Guid.NewGuid(), null, 1, "actor", now);
            var foreignRoute = new ApprovalRoute(Guid.NewGuid(), f.BranchId, "JV", 0, 100,
                Guid.NewGuid(), true, "actor", now);
            Ensure(CheckRefs(card, foreignSeries, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                CheckRefs(card, series, foreignRoute) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Cross-tenant or wrong-ID references accepted");
            var wrongIdSeries = new DocumentNumberSeries(f.CompanyId, f.BranchId,
                NumberingDocumentKind.Journal, Guid.NewGuid(), null, 1, "actor", now);
            var wrongIdRoute = new ApprovalRoute(f.CompanyId, f.BranchId, "JV", 0, 100,
                Guid.NewGuid(), true, "actor", now);
            Ensure(CheckRefs(card, wrongIdSeries, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                CheckRefs(card, series, wrongIdRoute) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Wrong reference IDs within tenant accepted");
            var generalSeries = new DocumentNumberSeries(f.CompanyId, null,
                NumberingDocumentKind.Journal, Guid.NewGuid(), null, 1, "actor", now);
            var generalRoute = new ApprovalRoute(f.CompanyId, null, "JV", 0, 100,
                Guid.NewGuid(), true, "actor", now);
            var scopedCard = new JournalDocumentType(f.CompanyId, f.BranchId, "JV-003", "قيد",
                JournalDocumentOrigin.Manual, generalSeries.Id, generalRoute.Id, false, "actor", now);
            Ensure(CheckRefs(scopedCard, generalSeries, generalRoute) ==
                JournalDocumentTypeReferenceStatus.UnresolvedPolicy,
                "General references should be structurally compatible with a specific branch");
            var wrongBranchSeries = new DocumentNumberSeries(f.CompanyId, Guid.NewGuid(),
                NumberingDocumentKind.Journal, Guid.NewGuid(), null, 1, "actor", now);
            var wrongBranchRoute = new ApprovalRoute(f.CompanyId, Guid.NewGuid(), "JV", 0, 100,
                Guid.NewGuid(), true, "actor", now);
            var seriesBranchCard = new JournalDocumentType(f.CompanyId, null, "JV-002", "قيد",
                JournalDocumentOrigin.Manual, series.Id, route.Id, false, "actor", now);
            Ensure(CheckRefs(card, wrongBranchSeries, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                CheckRefs(card, series, wrongBranchRoute) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                CheckRefs(seriesBranchCard, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Branch-specific references accepted for incompatible scope");
            route.SetActive(false, now.AddMinutes(1));
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InactiveReference,
                "Inactive approval route accepted");
            route.SetActive(true, now.AddMinutes(2));
            series.SetActive(false, now.AddMinutes(1));
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InactiveReference,
                "Inactive numbering series accepted");
            series.SetActive(true, now.AddMinutes(2));
            card.SetActive(false, now.AddMinutes(1));
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InactiveReference,
                "Inactive document type accepted");
            card.SetActive(true, now.AddMinutes(2));
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.UnresolvedPolicy,
                "Restored valid structural references should remain unresolved");
            typeof(ApprovalRoute).GetProperty(nameof(ApprovalRoute.Revision))!.SetValue(route, 0);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Corrupt hydrated approval route revision accepted");
            typeof(ApprovalRoute).GetProperty(nameof(ApprovalRoute.Revision))!.SetValue(route, 3);
            typeof(DocumentNumberSeries).GetProperty(nameof(DocumentNumberSeries.Revision))!
                .SetValue(series, 0);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Corrupt hydrated numbering revision accepted");
            typeof(DocumentNumberSeries).GetProperty(nameof(DocumentNumberSeries.Revision))!
                .SetValue(series, 1);
            // Persisted EF snapshots can bypass constructors. Reject invalid card and
            // numbering fields before even returning the deliberately unresolved policy.
            typeof(JournalDocumentType).GetProperty(nameof(JournalDocumentType.Code))!
                .SetValue(card, "-INVALID");
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Hydrated document code with invalid first character accepted");
            typeof(JournalDocumentType).GetProperty(nameof(JournalDocumentType.Code))!
                .SetValue(card, "JV-001");
            typeof(JournalDocumentType).GetProperty(nameof(JournalDocumentType.Revision))!
                .SetValue(card, 0);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Hydrated document with zero revision accepted");
            typeof(JournalDocumentType).GetProperty(nameof(JournalDocumentType.Revision))!
                .SetValue(card, 3);
            typeof(DocumentNumberSeries).GetProperty(nameof(DocumentNumberSeries.NextNumber))!
                .SetValue(series, 0L);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                "Hydrated numbering series with zero next number accepted");
            typeof(DocumentNumberSeries).GetProperty(nameof(DocumentNumberSeries.NextNumber))!
                .SetValue(series, 1L);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.UnresolvedPolicy,
                "Recovered hydrated references must remain unauthorized");
            return Task.CompletedTask;
        });
        test("GL14-02: malformed hydrated SYS10 prefix and SYS09 actor fail closed", f =>
        {
            var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
            var series = new DocumentNumberSeries(f.CompanyId, f.BranchId,
                NumberingDocumentKind.Journal, Guid.NewGuid(), "JV", 1, "actor", now);
            var route = new ApprovalRoute(f.CompanyId, f.BranchId, "JV", 0, null,
                Guid.NewGuid(), true, "actor", now);
            var card = new JournalDocumentType(f.CompanyId, f.BranchId, "JV-1", "قيد",
                JournalDocumentOrigin.Manual, series.Id, route.Id, false, "actor", now);
            static JournalDocumentTypeReferenceStatus CheckRefs(JournalDocumentType c,
                DocumentNumberSeries s, ApprovalRoute r) =>
                JournalDocumentTypeReferencePolicy.Evaluate(c, s, r);
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.UnresolvedPolicy,
                "Valid hydrated references should remain unresolved, not authorized");
            foreach (var prefix in new[] { "   ", " JV", "JV ", "JV\n", "JV..1" })
            {
                typeof(DocumentNumberSeries).GetProperty(nameof(DocumentNumberSeries.Prefix))!
                    .SetValue(series, prefix);
                Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot,
                    "Malformed hydrated numbering prefix accepted: " + prefix);
            }
            typeof(DocumentNumberSeries).GetProperty(nameof(DocumentNumberSeries.Prefix))!
                .SetValue(series, "JV");
            typeof(ApprovalRoute).GetProperty(nameof(ApprovalRoute.CreatedBy))!
                .SetValue(route, "actor\nadmin");
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.InvalidSnapshot &&
                !ApprovalRouteConflictPolicy.HasValidSnapshot(route),
                "Control characters in hydrated SYS09 actor accepted");
            typeof(ApprovalRoute).GetProperty(nameof(ApprovalRoute.CreatedBy))!
                .SetValue(route, "actor");
            Ensure(CheckRefs(card, series, route) == JournalDocumentTypeReferenceStatus.UnresolvedPolicy,
                "Recovered references should remain unresolved without approval policy");
            return Task.CompletedTask;
        });
        test("GL14-02 partial: stale revisions reject edit and activation atomically", f =>
        {
            var now = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero);
            var series = Guid.NewGuid(); var route = Guid.NewGuid();
            var card = new JournalDocumentType(f.CompanyId, f.BranchId, "001", "الأصل",
                JournalDocumentOrigin.Manual, series, route, false, "actor", now);
            var version = card.Version;
            Expect("stale_document_type_revision", () => card.Edit("جديد", Guid.NewGuid(),
                Guid.NewGuid(), true, 0, now.AddMinutes(1)));
            Expect("stale_document_type_revision", () => card.SetActive(false, 0, now.AddMinutes(1)));
            Ensure(card.Revision == 1 && card.Name == "الأصل" && card.NumberSeriesId == series &&
                card.ApprovalRouteId == route && !card.RequiresAttachment && card.IsActive &&
                card.Version == version, "Stale edit mutated the document type");
            card.Edit("جديد", series, route, true, 1, now.AddMinutes(1));
            card.SetActive(false, 2, now.AddMinutes(2));
            Ensure(card.Revision == 3 && !card.IsActive && card.RequiresAttachment &&
                card.Name == "جديد", "Current revisions did not update atomically");
            return Task.CompletedTask;
        });
        test("GL14 revision: stale edit and status cannot mutate references, state or audit", f =>
        {
            var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
            var originalSeries = Guid.NewGuid();
            var originalRoute = Guid.NewGuid();
            var card = new JournalDocumentType(f.CompanyId, f.BranchId, "GL14-R",
                "أصل", JournalDocumentOrigin.Manual, originalSeries, originalRoute,
                true, "actor", now);
            Ensure(card.MatchesSource(JournalDocumentOrigin.Manual) &&
                !card.MatchesSource(JournalDocumentOrigin.Automatic) &&
                !card.MatchesSource((JournalDocumentOrigin)999),
                "GL14 manual type can impersonate an automatic source");
            var originalVersion = card.Version;
            Expect("stale_document_type_revision", () =>
                card.Edit("مرفوض", Guid.NewGuid(), Guid.NewGuid(), false, 0, now.AddMinutes(1)));
            Expect("stale_document_type_revision", () =>
                card.SetActive(false, 0, now.AddMinutes(1)));
            Ensure(card.Name == "أصل" && card.RequiresAttachment &&
                card.NumberSeriesId == originalSeries && card.ApprovalRouteId == originalRoute &&
                card.IsActive && card.Revision == 1 && card.Version == originalVersion &&
                card.LastModifiedAt == now, "Stale GL14 update changed card or audit");
            card.Edit("مقبول", originalSeries, originalRoute, false, 1, now.AddMinutes(1));
            Ensure(card.Name == "مقبول" && !card.RequiresAttachment && card.Revision == 2,
                "Current revision edit was rejected");
            var editedVersion = card.Version;
            Expect("stale_document_type_revision", () =>
                card.SetActive(false, 1, now.AddMinutes(2)));
            Ensure(card.IsActive && card.Revision == 2 && card.Version == editedVersion,
                "Stale disable changed accepted revision");
            card.SetActive(false, 2, now.AddMinutes(2));
            Ensure(!card.MatchesSource(JournalDocumentOrigin.Manual),
                "GL14 disabled type remained source-compatible");
            Ensure(!card.IsActive && card.Revision == 3 &&
                card.LastModifiedAt == now.AddMinutes(2), "Current revision disable failed");
            Expect("invalid_document_type_revision", () =>
                card.Edit("قديم", originalSeries, originalRoute, false, 3, now));
            Ensure(card.Revision == 3 && card.Name == "مقبول",
                "Stale timestamp changed GL14");
            return Task.CompletedTask;
        });
        test("GL14-01: EF model has composite tenant FKs, unique code and blocked unvalidated route", async f =>
        {
            await using var db = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
                .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
            var model = db.Model.FindEntityType(typeof(JournalDocumentType))!;
            Ensure(model.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(DocumentNumberSeries) &&
                k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "CompanyId", "NumberSeriesId" })) &&
                model.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(Branch) &&
                k.Properties.Select(p => p.Name).Order().SequenceEqual(new[] { "BranchId", "CompanyId" })) &&
                model.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "CompanyId", "Code" })), "Tenant-scoped schema constraints missing");
            var card = new JournalDocumentType(f.CompanyId, f.BranchId, "X", "نوع",
                JournalDocumentOrigin.Manual, Guid.NewGuid(), Guid.NewGuid(), false, "actor", DateTimeOffset.UtcNow);
            db.Add(card);
            try { await db.SaveChangesAsync(); throw new Exception("Unvalidated SYS09 route was persisted"); }
            catch (BusinessException ex) when (ex.Code == "document_type_approval_route_unavailable") { }
        });
    }

    private static void Ensure(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static void Expect(string code, Action action)
    {
        try { action(); }
        catch (BusinessException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}
