using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureJournalDocumentType(ModelBuilder model)
    {
        var b = Tenant<JournalDocumentType>(model, "JournalDocumentTypes");
        b.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        b.HasOne<DocumentNumberSeries>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.NumberSeriesId })
            .OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApprovalRoute>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ApprovalRouteId })
            .OnDelete(DeleteBehavior.NoAction);
        // A real FK now exists, but the route still cannot be activated or validated.
        // Do not expose create/edit/activation until SYS09 role/policy checks exist.
        Constraint(b, "CK_JournalDocumentType_Origin", "[Origin] IN (0,1)");
        Constraint(b, "CK_JournalDocumentType_Revision",
            "[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");
    }
}
