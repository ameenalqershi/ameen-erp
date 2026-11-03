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
