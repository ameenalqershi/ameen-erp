# نقطة استئناف تطوير AMEEN ERP — 2026-11-02 04:30 UTC

## الأساس المتحقق

- المستودع الخاص: `ameenalqershi/ameen-erp`، `main` قبل الدفعة: `f70f80cb29bb7a008ea533cc0cb211cf9464023e`. CI `41383596304` ناجح على SHA نفسه، 334/334. لم تكن هناك تغييرات محلية أو git locks قبل إنشاء الفرع.
- `GL02-T01` مقبولة مسبقًا في Notion؛ لا تعِد تنفيذها. تبقى `GL02-A07/GP02` (SYS12)، `GP03` (SYS13)، `C01/GP04/GP05` (INV06/SAL03/PUR03/GL10 والسياسة) فجوات.
- لا توجد مهمة GL صغيرة مفتوحة في Notion بحالة `قيد التنفيذ` أو `قيد التحقق` وقت الفحص. تم اختيار `GL14-01` بوصفها أول مرحلة بيانات مستقلة وفق `gl-completion.md`.

## العمل الحالي القابل للاستئناف

- **المهمة:** `GL14-01` — أساس أنواع القيود والمستندات، **جزئي وغير مقبول بعد**.
- **الفرع:** `temp/gl14-01-schema-20261102`. لا تُعد تنفيذها من البداية؛ افحص `git status --short --branch` والفرع ورأس `origin/main` أولًا.
- **الملفات:** `backend/src/Ameen.Erp.Domain/Accounting/JournalDocumentType.cs`؛ `backend/src/Ameen.Erp.Infrastructure/Persistence/Configurations/JournalDocumentTypeModel.cs`؛ `backend/src/Ameen.Erp.Infrastructure/Persistence/ErpDbContext.cs` و`ErpDbContext.Validation.cs`؛ `backend/database/027_journal_document_types.sql`؛ `backend/tests/Ameen.Erp.Tests/JournalDocumentTypeFoundationTests.cs` و`JournalDocumentTypeSqlIntegration.cs` و`Program.cs` و`SqlIntegration.cs`؛ `backend/scripts/traceability.py`؛ `docs/backend/gl14-01-foundation.md`؛ `PROGRESS.md` ومصفوفاتها.
- **الاختبار المحلي:** `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts backend/.artifacts/gl14-01-20261102`، 336/336 ناجح دون SQL؛ التحقق بعد تحديث المصفوفات مطلوب قبل الدفع. اختبار SQL معزول متاح في CI فقط حتى الآن.
- **المانع:** SYS09 لم ينفذ مسارات اعتماد حقيقية؛ `ApprovalRouteId` لا يجوز اعتباره مرجعًا فعالًا، لذا يمنع حارس EF جميع تغييرات GL14. لا API ولا اعتماد GL14 أو GL03 بعد. لا تزيل الحارس دون FK وتحقيق نطاق الشركة والفرع والنشاط والسياسة.
- **SHA / CI للدفعة:** لم يُعتمد بعد؛ حدّث هذه الفقرة فقط بعد نجاح CI على الفرع نفسه. لا تدفع إلى `main` قبل نجاح SQL المعزول وHTTP/JWT/OpenAPI والبناء وtraceability/progress.
- **الخطوة التالية الدقيقة:** فحص شجرة العمل والعمليات النشطة؛ إعادة `traceability.py --check` و`progress.py --check` و`verify.py` بعد تحديث التوثيق، دفع فرع `temp` لتشغيل CI؛ إصلاح أي فشل على الفرع دون دفع main؛ بعد نجاح CI وتطابق SHA، fast-forward main، تحديث Notion بوضوح إلى `قيد التنفيذ` لا `مكتمل`، ثم متابعة SYS09 أو مهمة GL مستقلة.

## حدود السلامة

لا تغيّر `muhaseb-erp-analysis/` أو الأسرار أو قاعدة إنتاج؛ لا تكرر ترقية أو مهمة مقبولة، ولا تعلن اكتمال GL14 أو قسم GL. راجع [تفاصيل GL14](gl14-01-foundation.md) و[خطة GL](gl-completion.md).
