using Ameen.Erp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ameen.Erp.Infrastructure;

public sealed partial class ErpDbContext
{
    private void ValidateChanges()
    {
        ChangeTracker.DetectChanges();
        // INV16-01 is a field foundation, not an approved movement/GRNI policy.
        // No direction catalogue, counterpart/cost-center decision or route
        // activation contract has been signed off. All EF mutations fail closed.
        Check.That(!ChangeTracker.Entries<InventoryMovementReason>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "inventory_movement_reason_policy_unavailable",
            "حفظ أسباب الحركات محجوب حتى اعتماد الاتجاه والحساب المقابل ومسار الموافقة.",
            ErrorKind.Conflict);
        // SAL01-01 structural only. Customer classification (SAL15), country
        // identity rules, credit override authorization and SAL09 are unavailable.
        Check.That(!ChangeTracker.Entries<Customer>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "customer_policy_unavailable",
            "حفظ بطاقة العميل محجوب حتى اكتمال التصنيف والهوية والتفويض والائتمان.",
            ErrorKind.Conflict);

        // CASH09 transition list, authorized actor, cheque clearing, bank
        // debit and returned receivables are unapproved. Do not record an
        // apparent financial transition through any EF caller.
        Check.That(!ChangeTracker.Entries<ChequeMovement>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "cheque_movement_policy_unavailable",
            "لا يمكن حفظ حركة شيك قبل اعتماد حالات التحصيل والرفض والصرف والتسوية.",
            ErrorKind.Conflict);
        // CASH07/CASH08 document status, payee, cancellation, bank clearing and
        // accounting movements need approved linked CASH03/CASH04/CASH09
        // workflows. Fail closed for both headers and all EF write operations.
        Check.That(!ChangeTracker.Entries<IncomingCheque>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<OutgoingCheque>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "cheque_policy_unavailable",
            "لا يمكن حفظ شيكات قبض أو صرف قبل اعتماد دورة التحصيل والرفض والصرف.", ErrorKind.Conflict);
        // CASH03 receipt methods, payer/SAL11 invoices and cheque collection
        // are not approved yet. No caller may persist a header or line through EF.
        Check.That(!ChangeTracker.Entries<CashReceipt>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<CashReceiptLine>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "cash_receipt_policy_unavailable",
            "لا يمكن حفظ سند قبض قبل اعتماد التحصيل والفواتير والشيكات والترحيل.", ErrorKind.Conflict);
        // CASH06 HR03 holder, advance type values, settlement and additional
        // disbursement are unapproved; no financial-looking write is allowed.
        Check.That(!ChangeTracker.Entries<CashAdvance>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<CashAdvanceLine>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "cash_advance_policy_unavailable",
            "لا يمكن حفظ عهدة أو تسوية قبل اعتماد صاحب العهدة وربط المصروف ودورة الصرف.",
            ErrorKind.Conflict);
        // CASH10 is structural only. No approved statement import dedup,
        // bank/ledger movement binding, matching policy or GL journal exists.
        Check.That(!ChangeTracker.Entries<BankReconciliation>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<BankReconciliationLine>().Any(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "bank_reconciliation_policy_unavailable",
            "لا يمكن حفظ المطابقة قبل اعتماد حركات البنك والدفتر ومنع استيراد الكشف مرتين.",
            ErrorKind.Conflict);
        // CASH11 is structural-only. No approved count-to-ledger snapshot, adjustment
        // authorization, lifecycle or branch permissions exist yet. Prevent writes
        // through any EF caller, including child-only changes and deletions.
        Check.That(!ChangeTracker.Entries<CashCount>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<CashCountLine>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "cash_count_policy_unavailable",
            "لا يمكن حفظ الجرد قبل اعتماد الرصيد والتسوية والمراجعة.", ErrorKind.Conflict);
        // SYS08 user/role scope metadata has no approved grant/deny evaluator,
        // active status policy or account/customer group catalogues. Deny any
        // insert/update/delete, including line-only mutation through direct EF.
        Check.That(!ChangeTracker.Entries<UserDataScope>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<UserDataScopeLine>().Any(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "user_data_scope_policy_unavailable",
            "نطاق بيانات المستخدم محجوب حتى اعتماد سياسة التفويض ومجموعات الحسابات والعملاء.",
            ErrorKind.Conflict);
        // SYS15-01 has no approved operational restore privileges, isolated
        // validation, encryption key management or backup worker. No direct
        // EF caller may activate or mutate an unapproved job configuration.
        Check.That(!ChangeTracker.Entries<BackupJobConfiguration>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "backup_job_policy_unavailable",
            "إعدادات النسخ والاستعادة محجوبة حتى اعتماد التفويض والتشفير والاستعادة المعزولة.",
            ErrorKind.Conflict);
        // SYS13-01 stores only metadata. Import/replay/preview, balance control,
        // approvals and tenant-scoped file parsing need separate approved rules.
        Check.That(!ChangeTracker.Entries<DataImportBatch>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<DataImportFieldMapping>().Any(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "data_import_policy_unavailable",
            "الاستيراد محجوب حتى اعتماد المعاينة والتحقق من الرصيد والتكرار والصلاحيات.",
            ErrorKind.Conflict);
        // SYS12-01 is structural only. Country-required fields, legal print
        // content, template preview and watermarking have no approved policy.
        Check.That(!ChangeTracker.Entries<PrintTemplate>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "print_template_policy_unavailable",
            "حفظ قوالب الطباعة محجوب حتى اعتماد محتوى البلد ووسم المسودة وقواعد الصلاحية.",
            ErrorKind.Conflict);
        // SYS11-01: selectable catalogues, approval/versioning and posting-account
        // routing are not specified. Fail closed for headers and lines, including
        // direct EF callers, rather than silently enabling financial mappings.
        Check.That(!ChangeTracker.Entries<GeneralOptionsPolicy>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<GeneralOptionsAccountLine>().Any(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "general_options_policy_unavailable",
            "حفظ الخيارات والربط المحاسبي محجوب حتى اعتماد كتالوج القيم والتفويض وتاريخ السريان.",
            ErrorKind.Conflict);
        // SYS16-01: schema/domain foundation only. No verified payment-policy
        // transaction/API exists yet; even direct EF callers must fail closed.
        Check.That(!ChangeTracker.Entries<PaymentTerm>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<PaymentTermLine>().Any(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "payment_term_policy_unavailable",
            "حفظ طرق الدفع مؤجل حتى اعتماد الصلاحيات والتسوية ودورة المستند.");
        // CASH02 is schema-only until conditional IBAN / beneficiary-change review and permissions are approved.
        Check.That(!ChangeTracker.Entries<BankAccount>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "bank_account_policy_unavailable",
            "لا يمكن حفظ حساب بنكي قبل اعتماد التحقق من IBAN والمستفيد والصلاحيات.", ErrorKind.Conflict);
        Check.That(!ChangeTracker.Entries<CashBox>().Any(e => e.State == EntityState.Deleted),
            "cashbox_delete_prohibited", "بطاقة الصندوق لا تحذف؛ استخدم التعطيل.", ErrorKind.Conflict);
        // SYS07 roles have structural references only; no membership/permission enforcement yet.
        Check.That(!ChangeTracker.Entries<SecurityRole>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<SecurityRoleRule>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "security_role_policy_unavailable", "لا يمكن حفظ أدوار قبل تنفيذ الصلاحيات والنطاقات ومنع التعارض.", ErrorKind.Conflict);
        Check.That(!ChangeTracker.Entries<SecurityRoleAssignment>().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "security_role_assignment_policy_unavailable",
            "لا يمكن حفظ ربط دور بالمستخدم قبل اعتماد سياسة التفويض والتدقيق.", ErrorKind.Conflict);
        // SYS09 has a SYS07 role FK but no verified active/scope or overlap/amount approval policy yet.
        // Refuse all writes (including stage-only writes) rather than accepting invented roles.
        Check.That(!ChangeTracker.Entries<ApprovalRoute>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            !ChangeTracker.Entries<ApprovalRouteStage>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted),
            "approval_route_role_policy_unavailable", "لا يمكن حفظ مسار اعتماد قبل تنفيذ الأدوار والسياسات والتحقق من التعارض.", ErrorKind.Conflict);
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
