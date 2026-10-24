using Ameen.Erp.Domain;
namespace Ameen.Erp.Application;
internal static class InventoryOpeningReconciliation
{
    internal static bool Matches(InventoryOpening opening, JournalEntry? journal, IReadOnlyList<StockMovement> movements)
    {
        if (journal is null || opening.State != InventoryOpeningState.Posted || journal.State != JournalState.Posted ||
            journal.Id != opening.JournalId || journal.InventoryOpeningId != opening.Id || journal.Source != JournalSource.InventoryOpening ||
            journal.CompanyId != opening.CompanyId || journal.BranchId != opening.BranchId || journal.FiscalPeriodId != opening.FiscalPeriodId || journal.AccountingDate != opening.AccountingDate)
            return false;
        var value = opening.Lines.Sum(x => x.Value);
        if (journal.Lines.Count != 2 || journal.Lines.Sum(x => x.FunctionalDebit) != value || journal.Lines.Sum(x => x.FunctionalCredit) != value ||
            !journal.Lines.Any(x => x.AccountId == opening.InventoryAccountId && x.FunctionalDebit == value && x.FunctionalCredit == 0) ||
            !journal.Lines.Any(x => x.AccountId == opening.ClearingAccountId && x.FunctionalCredit == value && x.FunctionalDebit == 0)) return false;
        var groups = opening.Lines.GroupBy(x => new { x.ItemId, x.IdentityId }).ToArray();
        if (groups.Length != movements.Count) return false;
        return groups.All(g => movements.Count(m => m.CompanyId == opening.CompanyId && m.OpeningId == opening.Id && m.JournalId == journal.Id &&
            m.BranchId == opening.BranchId && m.WarehouseId == opening.WarehouseId && m.AccountingDate == opening.AccountingDate &&
            m.CurrencyCode == opening.CurrencyCode && m.InventoryAccountId == opening.InventoryAccountId && m.ItemId == g.Key.ItemId && m.IdentityId == g.Key.IdentityId &&
            m.BaseQuantity == g.Sum(x => x.BaseQuantity) && m.Value == g.Sum(x => x.Value) && m.IsSerial == g.Any(x => x.SerialNumber is not null)) == 1);
    }
}
