using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Infrastructure;
public sealed partial class ErpDbContext
{
    private static void ConfigureFinancialMappings(ModelBuilder model)
    {
        var m=Tenant<FinancialMapping>(model,"FinancialMappings");m.Property(x=>x.Name).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");m.Property(x=>x.VersionCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");m.Property(x=>x.CreatedBy).HasMaxLength(200);
        m.HasIndex(x=>new{x.CompanyId,x.BranchId,x.StatementKind,x.Name,x.VersionCode}).IsUnique().HasFilter(null);
        m.HasOne<Branch>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.BranchId}).OnDelete(DeleteBehavior.NoAction);
        m.HasMany(x=>x.Lines).WithOne().HasForeignKey(x=>new{x.CompanyId,x.MappingId}).OnDelete(DeleteBehavior.Cascade);m.Navigation(x=>x.Lines).AutoInclude();
        Constraint(m,"CK_FinancialMapping_Kind","[StatementKind] BETWEEN 0 AND 3");Constraint(m,"CK_FinancialMapping_Revision","[Revision]>0 AND [LastModifiedAt]>=[CreatedAt]");
        var l=Tenant<FinancialMappingLine>(model,"FinancialMappingLines");l.Property(x=>x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");l.Property(x=>x.ParentCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");l.Property(x=>x.Title).HasMaxLength(200);l.Property(x=>x.Formula).HasMaxLength(2000);
        l.HasAlternateKey(x=>new{x.CompanyId,x.MappingId,x.Id});l.HasIndex(x=>new{x.CompanyId,x.MappingId,x.Code}).IsUnique();l.HasIndex(x=>new{x.CompanyId,x.MappingId,x.Position}).IsUnique();
        l.HasOne<Account>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.AccountId}).OnDelete(DeleteBehavior.NoAction);
        l.HasOne<FinancialMappingLine>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.MappingId,x.ParentLineId}).HasPrincipalKey(x=>new{x.CompanyId,x.MappingId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        Constraint(l,"CK_FinancialMappingLine_Kind","[DisplaySign] IN (-1,1) AND [Position] BETWEEN 1 AND 500 AND ([AccountId] IS NULL OR [Formula] IS NULL)");
        Constraint(l,"CK_FinancialMappingLine_Parent","([ParentCode] IS NULL AND [ParentLineId] IS NULL) OR ([ParentCode] IS NOT NULL AND [ParentLineId] IS NOT NULL AND [ParentLineId]<>[Id])");
        var r=Tenant<FinancialMappingReview>(model,"FinancialMappingReviews");r.Property(x=>x.ReviewedBy).HasMaxLength(200);r.HasOne<FinancialMapping>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.MappingId}).OnDelete(DeleteBehavior.NoAction);
    }
}
