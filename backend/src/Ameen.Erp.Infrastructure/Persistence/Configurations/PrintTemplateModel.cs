using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigurePrintTemplates(ModelBuilder model)
    {
        var b = Tenant<PrintTemplate>(model, "PrintTemplates");
        b.Property(x => x.StatusCode).HasMaxLength(32);
        b.Property(x => x.DocumentTypeCode).HasMaxLength(64);
        b.Property(x => x.LanguageCode).HasMaxLength(32);
        b.Property(x => x.PaperSizeCode).HasMaxLength(32);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.VersionCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        b.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(b, "CK_PrintTemplate_Audit",
            "[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");
    }
}
