using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureBackupJobs(ModelBuilder model)
    {
        var b=Tenant<BackupJobConfiguration>(model,"BackupJobConfigurations");
        b.Property(x=>x.StatusCode).HasMaxLength(32);
        b.Property(x=>x.Name).HasMaxLength(200);
        b.Property(x=>x.ScheduleText).HasMaxLength(200);
        b.Property(x=>x.Destination).HasMaxLength(2000);
        b.Property(x=>x.LastTestReference).HasMaxLength(200);
        b.Property(x=>x.CreatedBy).HasMaxLength(200);
        b.Property(x=>x.CreatedAt).HasColumnType("datetimeoffset(7)");
        b.Property(x=>x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x=>new{x.CompanyId,x.BranchId})
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(b,"CK_BackupJob_Retention","[RetentionPolicy]>=0");
        Constraint(b,"CK_BackupJob_Audit","[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");
    }
}
