using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Infrastructure;
public sealed partial class ErpDbContext
{
    private static void ConfigureDocumentNumbering(ModelBuilder model)
    {
        var s=Tenant<DocumentNumberSeries>(model,"DocumentNumberSeries");
        s.Property(x=>x.Prefix).HasMaxLength(32);s.Property(x=>x.CreatedBy).HasMaxLength(200);
        s.HasIndex(x=>new{x.CompanyId,x.DocumentKind,x.FiscalYearId,x.BranchId}).IsUnique().HasFilter(null);
        s.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.BranchId}).OnDelete(DeleteBehavior.NoAction);
        s.HasOne<FiscalYear>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.FiscalYearId}).OnDelete(DeleteBehavior.NoAction);
        Constraint(s,"CK_NumberSeries_Kind","[DocumentKind] IN (0,1)");
        Constraint(s,"CK_NumberSeries_Next","[NextNumber]>0 AND [NextNumber]<9223372036854775807");
        Constraint(s,"CK_NumberSeries_Revision","[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");
        var a=Tenant<DocumentNumberAllocation>(model,"DocumentNumberAllocations");
        a.Property(x=>x.Number).HasMaxLength(64);a.Property(x=>x.ReservedBy).HasMaxLength(200);
        a.HasIndex(x=>new{x.CompanyId,x.DocumentKind,x.Number}).IsUnique();
        a.HasIndex(x=>new{x.CompanyId,x.SeriesId,x.Sequence}).IsUnique();
        a.HasOne<DocumentNumberSeries>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.SeriesId}).OnDelete(DeleteBehavior.NoAction);
        a.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.BranchId}).OnDelete(DeleteBehavior.NoAction);
        a.HasOne<JournalEntry>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.JournalId}).OnDelete(DeleteBehavior.NoAction);
        a.HasOne<InventoryOpening>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.InventoryOpeningId}).OnDelete(DeleteBehavior.NoAction);
        Constraint(a,"CK_NumberAllocation_Document","([DocumentId] IS NULL AND [JournalId] IS NULL AND [InventoryOpeningId] IS NULL) OR ([DocumentKind]=0 AND [DocumentId] IS NOT NULL AND [JournalId] IS NOT NULL AND [DocumentId]=[JournalId] AND [InventoryOpeningId] IS NULL) OR ([DocumentKind]=1 AND [DocumentId] IS NOT NULL AND [InventoryOpeningId] IS NOT NULL AND [DocumentId]=[InventoryOpeningId] AND [JournalId] IS NULL)");
        Constraint(a,"CK_NumberAllocation_Positive","[Sequence]>0 AND [DocumentKind] IN (0,1)");
    }
}
