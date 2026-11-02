using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private void ValidateChanges()
    {
        ChangeTracker.DetectChanges();
        // GL14 cards require an active SYS09 approval route. Until SYS09 is implemented,
        // prevent even internal EF callers from persisting an unvalidated route reference.
        Check.That(!ChangeTracker.Entries<JournalDocumentType>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "document_type_approval_route_unavailable", "مسارات الاعتماد SYS09 غير متاحة بعد؛ لا يمكن حفظ نوع مستند.", ErrorKind.Conflict);
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
        foreach (var entry in ChangeTracker.Entries<DocumentNumberAllocation>())
        {
            Check.That(entry.State is not (EntityState.Modified or EntityState.Deleted),"number_allocation_immutable","حجز الرقم سجل تاريخي لا يعدل أو يحذف.");
            if(entry.State==EntityState.Added && entry.Entity.DocumentId is Guid id)
            {
                var a=entry.Entity;
                var linked=a.DocumentKind==NumberingDocumentKind.Journal
                    ? ChangeTracker.Entries<JournalEntry>().Any(x=>x.Entity.CompanyId==a.CompanyId && x.Entity.Id==id && x.Entity.Number==a.Number && x.Entity.BranchId==a.BranchId && x.State is EntityState.Added or EntityState.Modified)
                    : ChangeTracker.Entries<InventoryOpening>().Any(x=>x.Entity.CompanyId==a.CompanyId && x.Entity.Id==id && x.Entity.Number==a.Number && x.Entity.BranchId==a.BranchId && x.State==EntityState.Added);
                Check.That(linked,"number_document_owner_required","ربط الحجز يتطلب مستنده ضمن معاملة الإنشاء أو نقل سنة المسودة.");
            }
        }
        foreach(var entry in ChangeTracker.Entries<FinancialMappingReview>())
            Check.That(entry.State is not(EntityState.Modified or EntityState.Deleted),"mapping_review_immutable","لقطة مراجعة الربط لا تعدل أو تحذف.");
        foreach(var entry in ChangeTracker.Entries<FinancialMappingLine>().Where(x=>x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            Check.That(ChangeTracker.Entries<FinancialMapping>().Any(x=>x.Entity.CompanyId==entry.Entity.CompanyId && x.Entity.Id==entry.Entity.MappingId &&
                (x.State==EntityState.Added || x.State==EntityState.Modified && x.Property(a=>a.Version).IsModified)),"mapping_lines_owner_required","تعديل البنود يتطلب النموذج ضمن المعاملة.");
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
            var sourceFields = new[] { nameof(JournalEntry.Source), nameof(JournalEntry.OpeningCurrencyCode), nameof(JournalEntry.OpeningExchangeRate), nameof(JournalEntry.InventoryOpeningId), nameof(JournalEntry.RecurringTemplateId), nameof(JournalEntry.SourceTemplateVersion), nameof(JournalEntry.ReversesJournalId) };
            Check.That(!entry.Properties.Any(p => p.IsModified && sourceFields.Contains(p.Metadata.Name)),
                "journal_source_immutable", "لا يمكن تغيير مصدر قيد محفوظ.");
            var previous = entry.OriginalValues.GetValue<JournalState>(nameof(JournalEntry.State));
            var headerFields = new[] { nameof(JournalEntry.BranchId), nameof(JournalEntry.FiscalPeriodId), nameof(JournalEntry.Number), nameof(JournalEntry.Sequence), nameof(JournalEntry.DocumentDate), nameof(JournalEntry.AccountingDate), nameof(JournalEntry.ExternalReference), nameof(JournalEntry.OperationStartsOn) };
            if (entry.Properties.Any(p => p.IsModified && headerFields.Contains(p.Metadata.Name)))
                Check.That(previous == JournalState.Draft && entry.Entity.State == JournalState.Draft &&
                    entry.Entity.Source is JournalSource.Manual or JournalSource.GeneralOpening,
                    "journal_header_immutable", "تعديل الرأس متاح لمسودة قيد يدوي أو افتتاح عام فقط.");
            if (previous is not (JournalState.Posted or JournalState.Reversed)) continue;
            Check.That(entry.State != EntityState.Deleted, "posted_journal_immutable", "لا يمكن حذف قيد مرحل.");
            var allowed = new[] { nameof(JournalEntry.State), nameof(JournalEntry.ReversalJournalId), nameof(JournalEntry.PendingReversalJournalId), nameof(JournalEntry.Version) };
            if (entry.Entity.Source == JournalSource.GeneralOpening &&
                (entry.Property(x => x.ReversalJournalId).IsModified || entry.Property(x => x.PendingReversalJournalId).IsModified))
            {
                Check.That(entry.OriginalValues.GetValue<int?>(nameof(JournalEntry.OpeningRevision)) is int revision &&
                    revision < int.MaxValue && entry.Entity.OpeningRevision == revision + 1 &&
                    entry.Entity.OpeningModifiedAt is not null,
                    "opening_revision_required", "تغيير رابط عكس الافتتاح يتطلب تحديث إصداره ووقت تعديله.");
                allowed = [.. allowed, nameof(JournalEntry.OpeningRevision), nameof(JournalEntry.OpeningModifiedAt)];
            }
            if (entry.Property(x => x.ReversalJournalId).IsModified || entry.Property(x => x.PendingReversalJournalId).IsModified)
            {
                Check.That(entry.OriginalValues.GetValue<int>(nameof(JournalEntry.Revision)) < int.MaxValue &&
                    entry.Entity.Revision == entry.OriginalValues.GetValue<int>(nameof(JournalEntry.Revision)) + 1 &&
                    entry.Entity.LastModifiedAt >= entry.Entity.CreatedAt,
                    "journal_revision_required", "تغيير رابط العكس يتطلب زيادة إصدار القيد مرة واحدة ووقت تعديل خادمي.");
                allowed = [.. allowed, nameof(JournalEntry.Revision), nameof(JournalEntry.LastModifiedAt)];
            }
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
