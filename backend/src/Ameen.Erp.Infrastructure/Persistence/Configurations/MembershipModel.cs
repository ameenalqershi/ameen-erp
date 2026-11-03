using System.Text.Json;
using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureMembership(ModelBuilder model)
    {
        var member = Tenant<Membership>(model, "Memberships");
        member.Property(x => x.Subject).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        member.HasIndex(x => new { x.CompanyId, x.Subject }).IsUnique();
        member.Property(x => x.CreatedBy).HasMaxLength(200);
        member.Property(x => x.CreatedAt).HasColumnType("datetimeoffset(7)");
        member.Property(x => x.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        Constraint(member, "CK_Membership_AuditMetadata",
            "([CreatedBy] IS NULL AND [CreatedAt] IS NULL AND [Revision] IS NULL AND [LastModifiedAt] IS NULL)" +
            " OR ([CreatedBy] IS NOT NULL AND LEN(LTRIM(RTRIM([CreatedBy]))) > 0" +
            " AND [CreatedAt] IS NOT NULL AND [Revision] >= 1 AND [LastModifiedAt] >= [CreatedAt])");
        member.Property(x => x.GrantedPermissions).HasConversion(
            x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
            x => JsonSerializer.Deserialize<string[]>(x, (JsonSerializerOptions?)null)!).Metadata.SetValueComparer(
                new ValueComparer<string[]>((a, b) => a!.SequenceEqual(b!),
                    x => x.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())), x => x.ToArray()));
        member.Property(x => x.BranchIds).HasConversion(
            x => JsonSerializer.Serialize(x, (JsonSerializerOptions?)null),
            x => JsonSerializer.Deserialize<Guid[]>(x, (JsonSerializerOptions?)null)!).Metadata.SetValueComparer(
                new ValueComparer<Guid[]>((a, b) => a!.SequenceEqual(b!),
                    x => x.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())), x => x.ToArray()));
    }
}
