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
            Expect("invalid_document_type_revision", () => card.SetActive(false, now.AddSeconds(-1)));
            Ensure(card.IsActive && card.Revision == 1, "Invalid timestamp mutated card");
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
