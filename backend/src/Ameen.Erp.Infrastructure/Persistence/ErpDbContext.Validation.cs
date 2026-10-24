using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private void ValidateChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<InventoryOpeningLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var owner = ChangeTracker.Entries<InventoryOpening>().SingleOrDefault(e => e.Entity.CompanyId == entry.Entity.CompanyId && e.Entity.Id == entry.Entity.OpeningId);
            Check.That(owner is not null && owner.Entity.State == InventoryOpeningState.Draft &&
                (owner.State == EntityState.Added || owner.State == EntityState.Modified && owner.Property(x => x.Version).IsModified &&
                 owner.OriginalValues.GetValue<InventoryOpeningState>(nameof(InventoryOpening.State)) is InventoryOpeningState.Draft or InventoryOpeningState.Returned),
                "opening_lines_immutable", "السطور تعدل داخل مسودة أو تصحيح محمل فقط.");
        }
        foreach (var entry in ChangeTracker.Entries<InventoryOpening>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            Check.That(!entry.Property(x => x.ReversesOpeningId).IsModified, "opening_reversal_source_immutable", "مرجع العكس غير قابل للتعديل.");
            var original = entry.OriginalValues.GetValue<InventoryOpeningState>(nameof(InventoryOpening.State));
            Check.That(entry.State != EntityState.Deleted && original is not (InventoryOpeningState.Posted or InventoryOpeningState.Cancelled),
                "opening_immutable", "المستند المرحل أو الملغى محمي من التعديل.");
            if (original == InventoryOpeningState.Approved)
            {
                var allowed = new[] { nameof(InventoryOpening.State), nameof(InventoryOpening.JournalId), nameof(InventoryOpening.InventoryAccountId), nameof(InventoryOpening.ClearingAccountId), nameof(InventoryOpening.PostingSettingsVersion), nameof(InventoryOpening.PostedBy), nameof(InventoryOpening.PostedAt), nameof(InventoryOpening.Version), nameof(InventoryOpening.Revision), nameof(InventoryOpening.ModifiedAt) };
                Check.That(entry.Entity.State == InventoryOpeningState.Posted && !entry.Properties.Any(p => p.IsModified && !allowed.Contains(p.Metadata.Name)), "opening_immutable", "المعتمد يقبل أثر الترحيل فقط.");
            }
        }
        foreach (var entry in ChangeTracker.Entries<StockMovement>())
        {
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "stock_movement_immutable", "حركة المخزون تاريخية غير قابلة للتعديل.");
            if (entry.State == EntityState.Added)
                Check.That(ChangeTracker.Entries<InventoryOpening>().Any(x => x.State == EntityState.Modified && x.Entity.CompanyId == entry.Entity.CompanyId &&
                    x.Entity.Id == entry.Entity.OpeningId && x.Entity.State == InventoryOpeningState.Posted && x.Entity.JournalId == entry.Entity.JournalId &&
                    x.OriginalValues.GetValue<InventoryOpeningState>(nameof(InventoryOpening.State)) == InventoryOpeningState.Approved),
                    "stock_posting_owner_required", "الحركة تتطلب ترحيل مصدر معتمد ضمن المعاملة.");
        }
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "audit_immutable", "سجل التدقيق غير قابل للتعديل أو الحذف.");
        foreach (var entry in ChangeTracker.Entries<ExchangeRate>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "exchange_rate_immutable", "سعر الصرف التاريخي غير قابل للتعديل أو الحذف.");
        foreach (var entry in ChangeTracker.Entries<IdempotencyRecord>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "receipt_immutable", "نتيجة الطلب المحفوظة غير قابلة للتعديل.");
        foreach (var entry in ChangeTracker.Entries<RecurringJournalGeneration>())
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted), "recurring_generation_immutable", "سجل توليد القيد غير قابل للتعديل أو الحذف.");
        foreach (var entry in ChangeTracker.Entries<WarehouseLocation>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            Check.That(ChangeTracker.Entries<Warehouse>().Any(w => w.Entity.CompanyId == entry.Entity.CompanyId && w.Entity.Id == entry.Entity.WarehouseId &&
                (w.State == EntityState.Added || (w.State == EntityState.Modified && w.Property(x => x.Version).IsModified))),
                "warehouse_locations_owner_required", "تعديل المواقع يتطلب تحميل المخزن وتغيير إصداره.");
        foreach (var entry in ChangeTracker.Entries<ItemUnit>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            Check.That(ChangeTracker.Entries<Item>().Any(t => t.Entity.CompanyId == entry.Entity.CompanyId && t.Entity.Id == entry.Entity.ItemId &&
                (t.State == EntityState.Added || (t.State == EntityState.Modified && t.Property(x => x.Version).IsModified))),
                "item_units_owner_required", "تعديل وحدات الصنف يتطلب تحميل البطاقة وتغيير إصدارها.");
        foreach (var entry in ChangeTracker.Entries<RecurringJournalLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            Check.That(ChangeTracker.Entries<RecurringJournalTemplate>().Any(t => t.Entity.CompanyId == entry.Entity.CompanyId && t.Entity.Id == entry.Entity.TemplateId &&
                (t.State == EntityState.Added || (t.State == EntityState.Modified && t.Property(x => x.Version).IsModified))),
                "recurring_lines_owner_required", "تعديل السطور يتطلب تحميل القالب ضمن المعاملة.");
        foreach (var entry in ChangeTracker.Entries<JournalLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var owner = ChangeTracker.Entries<JournalEntry>().SingleOrDefault(e => e.Entity.CompanyId == entry.Entity.CompanyId && e.Entity.Id == entry.Entity.JournalId);
            var generatedOpening = owner is not null && owner.State == EntityState.Added && owner.Entity.Source == JournalSource.InventoryOpening && owner.Entity.State == JournalState.Posted &&
                ChangeTracker.Entries<InventoryOpening>().Any(x => x.State == EntityState.Modified && x.Entity.CompanyId == owner.Entity.CompanyId && x.Entity.Id == owner.Entity.InventoryOpeningId &&
                    x.Entity.State == InventoryOpeningState.Posted && x.Entity.JournalId == owner.Entity.Id);
            Check.That(generatedOpening || owner is not null && owner.Entity.State == JournalState.Draft &&
                (owner.State == EntityState.Added || owner.OriginalValues.GetValue<JournalState>(nameof(JournalEntry.State)) == JournalState.Draft),
                "journal_lines_immutable", "تعديل السطور مسموح داخل مسودة محملة فقط.");
        }
        foreach (var entry in ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Source == JournalSource.InventoryOpening)
            {
                var opening = ChangeTracker.Entries<InventoryOpening>().SingleOrDefault(x => x.State == EntityState.Modified && x.Entity.CompanyId == entry.Entity.CompanyId && x.Entity.Id == entry.Entity.InventoryOpeningId);
                var movements = ChangeTracker.Entries<StockMovement>().Where(x => x.State == EntityState.Added && x.Entity.CompanyId == entry.Entity.CompanyId && x.Entity.OpeningId == entry.Entity.InventoryOpeningId).Select(x => x.Entity).ToArray();
                Check.That(opening is not null && opening.Entity.State == InventoryOpeningState.Posted && opening.Entity.JournalId == entry.Entity.Id &&
                    entry.Entity.State == JournalState.Posted && entry.Entity.ApprovedBy == opening.Entity.ApprovedBy && entry.Entity.CreatedBy == opening.Entity.CreatedBy &&
                    movements.Sum(x => x.Value) == (opening.Entity.ReversesOpeningId is null ? 1 : -1) * entry.Entity.Lines.Sum(x => x.FunctionalDebit) && movements.Length > 0,
                    "inventory_journal_owner_required", "القيد الآلي يتطلب مصدرًا وحركات مخزون داخل معاملة الترحيل.");
            }
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            var sourceFields = new[] { nameof(JournalEntry.Source), nameof(JournalEntry.OperationStartsOn), nameof(JournalEntry.OpeningCurrencyCode), nameof(JournalEntry.OpeningExchangeRate), nameof(JournalEntry.InventoryOpeningId), nameof(JournalEntry.RecurringTemplateId), nameof(JournalEntry.SourceTemplateVersion), nameof(JournalEntry.ReversesJournalId) };
            Check.That(!entry.Properties.Any(p => p.IsModified && sourceFields.Contains(p.Metadata.Name)),
                "journal_source_immutable", "لا يمكن تغيير مصدر قيد محفوظ.");
            var previous = entry.OriginalValues.GetValue<JournalState>(nameof(JournalEntry.State));
            if (previous is not (JournalState.Posted or JournalState.Reversed)) continue;
            Check.That(entry.State != EntityState.Deleted, "posted_journal_immutable", "لا يمكن حذف قيد مرحل.");
            var allowed = new[] { nameof(JournalEntry.State), nameof(JournalEntry.ReversalJournalId), nameof(JournalEntry.PendingReversalJournalId), nameof(JournalEntry.Version) };
            Check.That(!entry.Properties.Any(p => p.IsModified && !allowed.Contains(p.Metadata.Name)),
                "posted_journal_immutable", "لا يمكن تعديل المحتوى المالي لقيد مرحل.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
