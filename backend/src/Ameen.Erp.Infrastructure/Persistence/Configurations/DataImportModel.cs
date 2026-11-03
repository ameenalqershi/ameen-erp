using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureDataImports(ModelBuilder model)
    {
        var batch = Tenant<DataImportBatch>(model, "DataImportBatches");
        batch.Property(x => x.StatusCode).HasMaxLength(32);
        batch.Property(x => x.DataKindCode).HasMaxLength(64);
        batch.Property(x => x.FileIdentifier).HasMaxLength(200);
        batch.Property(x => x.DuplicatePolicyCode).HasMaxLength(64);
        batch.Property(x => x.CreatedBy).HasMaxLength(200);
        batch.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        batch.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        batch.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        batch.HasMany(x => x.Mappings).WithOne()
            .HasForeignKey(x => new { x.CompanyId, x.BatchId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(batch, "CK_DataImport_Audit",
            "[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");

        var mapping = Tenant<DataImportFieldMapping>(model, "DataImportFieldMappings");
        mapping.Property(x => x.SourceColumn).HasMaxLength(200);
        mapping.Property(x => x.TargetField).HasMaxLength(200);
        mapping.Property(x => x.DefaultValue).HasMaxLength(2000);
    }
}
