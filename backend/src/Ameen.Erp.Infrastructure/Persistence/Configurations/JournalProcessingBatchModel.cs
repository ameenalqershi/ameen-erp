using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameen.Erp.Infrastructure;
public sealed partial class ErpDbContext
{
    private static void ConfigureJournalProcessingBatch(ModelBuilder model)
    {
        var b = Tenant<JournalProcessingBatch>(model, "JournalProcessingBatches");
        b.Property(x => x.BatchKey).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.Action).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.ActorId).HasMaxLength(200).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.BatchKey }).IsUnique();
        Constraint(b, "CK_ProcessingBatch_Count", "[ItemCount] BETWEEN 1 AND 100");
        Constraint(b, "CK_ProcessingBatch_Revision",
            "[Revision] >= 1 AND [LastModifiedAt] >= [CreatedAt] AND ([FinishedAt] IS NULL OR [FinishedAt] >= [CreatedAt])");
    }
}
