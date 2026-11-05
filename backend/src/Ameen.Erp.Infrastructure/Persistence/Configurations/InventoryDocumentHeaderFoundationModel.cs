using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private static void ConfigureInventoryDocumentHeaderFoundations(ModelBuilder model)
    {
        ConfigureFiscalScope<InventoryReceiptHeader>(model, "InventoryReceiptHeaders");
        ConfigureFiscalScope<InventoryIssueHeader>(model, "InventoryIssueHeaders");
        ConfigureFiscalScope<InventoryTransferHeader>(model, "InventoryTransferHeaders");
        ConfigureFiscalScope<InventoryTransferReceiptHeader>(model, "InventoryTransferReceiptHeaders");
        ConfigureFiscalScope<InventoryAdjustmentHeader>(model, "InventoryAdjustmentHeaders");
        ConfigureFiscalScope<InventoryDamageHeader>(model, "InventoryDamageHeaders");

        var count = Tenant<InventoryCountHeader>(model, "InventoryCountHeaders");
        count.Property(x => x.StatusCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        count.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        count.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.CompanyId, x.WarehouseId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(count, "CK_InventoryCountHeader_Status", "LEN(LTRIM(RTRIM([StatusCode]))) > 0");
        Constraint(count, "CK_InventoryCountHeader_References",
            "[WarehouseId] <> '00000000-0000-0000-0000-000000000000' AND ([BranchId] IS NULL OR [BranchId] <> '00000000-0000-0000-0000-000000000000')");

        var revaluation = Tenant<InventoryRevaluationHeader>(model, "InventoryRevaluationHeaders");
        revaluation.Property(x => x.StatusCode).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
        revaluation.Property(x => x.FromDate).HasColumnType("date");
        revaluation.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BranchId })
            .OnDelete(DeleteBehavior.NoAction);
        Constraint(revaluation, "CK_InventoryRevaluationHeader_Status",
            "LEN(LTRIM(RTRIM([StatusCode]))) > 0");
        Constraint(revaluation, "CK_InventoryRevaluationHeader_FromDate",
            "[FromDate] > '0001-01-01' AND ([BranchId] IS NULL OR [BranchId] <> '00000000-0000-0000-0000-000000000000')");
    }

    private static void ConfigureFiscalScope<T>(ModelBuilder model, string table)
        where T : CompanyEntity
    {
        var header = Tenant<T>(model, table);
        header.HasOne<Branch>().WithMany().HasForeignKey("CompanyId", "BranchId")
            .OnDelete(DeleteBehavior.NoAction);
        header.HasOne<FiscalYear>().WithMany().HasForeignKey("CompanyId", "FiscalYearId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
