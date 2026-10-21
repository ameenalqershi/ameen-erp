using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private void ValidateChanges()
    {
        ChangeTracker.DetectChanges();
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
            Check.That(owner is not null && owner.Entity.State == JournalState.Draft &&
                (owner.State == EntityState.Added || owner.OriginalValues.GetValue<JournalState>(nameof(JournalEntry.State)) == JournalState.Draft),
                "journal_lines_immutable", "تعديل السطور مسموح داخل مسودة محملة فقط.");
        }
        foreach (var entry in ChangeTracker.Entries<JournalEntry>())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            var sourceFields = new[] { nameof(JournalEntry.Source), nameof(JournalEntry.RecurringTemplateId), nameof(JournalEntry.SourceTemplateVersion), nameof(JournalEntry.ReversesJournalId) };
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
