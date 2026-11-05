using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureSalesRepresentatives(ModelBuilder model)
    {
        var reps = Tenant<SalesRepresentative>(model, "SalesRepresentatives");
        reps.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        reps.Property(x => x.Name).HasMaxLength(200);
        reps.Property(x => x.EnglishName).HasMaxLength(200);
        reps.Property(x => x.Notes).HasMaxLength(2000);
        reps.Property(x => x.Region).HasMaxLength(200);
        reps.Property(x => x.CreatedBy).HasMaxLength(200);
        reps.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        reps.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        reps.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        // Custody account has a real tenant-scoped provider. Employee, supervisor
        // and commission-policy references remain opaque until HR03/SAL10 contracts exist.
        reps.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CustodyAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(reps, "CK_SalesRepresentative_Audit",
            "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");
        Constraint(reps, "CK_SalesRepresentative_References",
            "([EmployeeId] IS NULL OR [EmployeeId] <> '00000000-0000-0000-0000-000000000000') AND " +
            "([SupervisorId] IS NULL OR [SupervisorId] <> '00000000-0000-0000-0000-000000000000') AND " +
            "([CustodyAccountId] IS NULL OR [CustodyAccountId] <> '00000000-0000-0000-0000-000000000000') AND " +
            "([CommissionPolicyId] IS NULL OR [CommissionPolicyId] <> '00000000-0000-0000-0000-000000000000')");
    }

    private static void ConfigureCustomerGroups(ModelBuilder model)
    {
        var groups = Tenant<CustomerGroup>(model, "CustomerGroups");
        groups.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        groups.Property(x => x.Name).HasMaxLength(200);
        groups.Property(x => x.EnglishName).HasMaxLength(200);
        groups.Property(x => x.Notes).HasMaxLength(2000);
        groups.Property(x => x.CreatedBy).HasMaxLength(200);
        groups.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        groups.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        groups.Property(x => x.DefaultCreditLimit).HasPrecision(20, 6);
        groups.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        groups.HasOne<CustomerGroup>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ParentGroupId })
            .OnDelete(DeleteBehavior.NoAction);
        groups.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CreditCurrencyId })
            .OnDelete(DeleteBehavior.NoAction);
        groups.HasOne<PaymentTerm>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PaymentTermId })
            .OnDelete(DeleteBehavior.NoAction);
        groups.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ControlAccountId })
            .OnDelete(DeleteBehavior.NoAction);
        // PriceListId remains an opaque required reference: SAL02 has no provider yet.
        Constraint(groups, "CK_CustomerGroup_Audit",
            "[Revision] > 0 AND [LastModifiedAt] >= [CreatedAt]");
        Constraint(groups, "CK_CustomerGroup_Credit",
            "[DefaultCreditLimit] >= 0");
        Constraint(groups, "CK_CustomerGroup_References",
            "[PriceListId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "[CreditCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "[PaymentTermId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "[ControlAccountId] <> '00000000-0000-0000-0000-000000000000' AND " +
            "([ParentGroupId] IS NULL OR [ParentGroupId] <> '00000000-0000-0000-0000-000000000000')");
    }
}
