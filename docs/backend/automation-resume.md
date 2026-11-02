# نقطة استئناف تطوير AMEEN ERP — 2026-11-02 04:30 UTC

## الأساس المتحقق

- المستودع الخاص: `ameenalqershi/ameen-erp`، `main` قبل الدفعة: `f70f80cb29bb7a008ea533cc0cb211cf9464023e`. CI `41383596304` ناجح على SHA نفسه، 334/334. لم تكن هناك تغييرات محلية أو git locks قبل إنشاء الفرع.
- `GL02-T01` مقبولة مسبقًا في Notion؛ لا تعِد تنفيذها. تبقى `GL02-A07/GP02` (SYS12)، `GP03` (SYS13)، `C01/GP04/GP05` (INV06/SAL03/PUR03/GL10 والسياسة) فجوات.
- لا توجد مهمة GL صغيرة مفتوحة في Notion بحالة `قيد التنفيذ` أو `قيد التحقق` وقت الفحص. تم اختيار `GL14-01` بوصفها أول مرحلة بيانات مستقلة وفق `gl-completion.md`.

## العمل الحالي القابل للاستئناف

- **المهمة:** `GL14-01` — أساس أنواع القيود والمستندات، **جزئي وغير مقبول بعد**.
- **الفرع:** `temp/gl14-01-schema-20261102`. لا تُعد تنفيذها من البداية؛ افحص `git status --short --branch` والفرع ورأس `origin/main` أولًا.
- **الملفات:** `backend/src/Ameen.Erp.Domain/Accounting/JournalDocumentType.cs`؛ `backend/src/Ameen.Erp.Infrastructure/Persistence/Configurations/JournalDocumentTypeModel.cs`؛ `backend/src/Ameen.Erp.Infrastructure/Persistence/ErpDbContext.cs` و`ErpDbContext.Validation.cs`؛ `backend/database/027_journal_document_types.sql`؛ `backend/tests/Ameen.Erp.Tests/JournalDocumentTypeFoundationTests.cs` و`JournalDocumentTypeSqlIntegration.cs` و`Program.cs` و`SqlIntegration.cs`؛ `backend/scripts/traceability.py`؛ `docs/backend/gl14-01-foundation.md`؛ `PROGRESS.md` ومصفوفاتها.
- **الاختبار المحلي:** `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts backend/.artifacts/gl14-01-final`، 336/336 ناجح دون SQL. **CI #41389013770 ناجح 337/337** على `a336d36337c0d2ce27943a999b247e4ce5a56104` مع SQL Server معزول (ذرية وإعادة 027 وحارس الحفظ)، HTTP/JWT وOpenAPI وRelease؛ بصمة المصدر `0c091f534aa82c47fd5b583dd98c6971785e35c5f02a12ef3b0b6ba81a3b0a55`.
- **المانع:** SYS09 لم ينفذ مسارات اعتماد حقيقية؛ `ApprovalRouteId` لا يجوز اعتباره مرجعًا فعالًا، لذا يمنع حارس EF جميع تغييرات GL14. لا API ولا اعتماد GL14 أو GL03 بعد. لا تزيل الحارس دون FK وتحقيق نطاق الشركة والفرع والنشاط والسياسة.
- **SHA / CI للدفعة:** commit الكود `a336d36337c0d2ce27943a999b247e4ce5a56104` على `temp/gl14-01-schema-20261102`، [CI ناجح #41389013770](https://github.com/ameenalqershi/ameen-erp/actions/runs/41389013770). لم يكن قد رُفع إلى `main` وقت تحديث هذا السجل؛ يجب التأكد من SHA الجديد وCI الخاص بتحديث التوثيق ثم fast-forward main.
- **الخطوة التالية الدقيقة:** تم تحديث Notion GL14-01 إلى `قيد التنفيذ` و`جزئي`، مع اعتماد SYS09 المانع. تحقق من شجرة العمل؛ سجّل تقرير CI الجديد في `verification-results.json` وشغّل `traceability.py --check` و`progress.py --check`، ثم ادفع commit التوثيق باسم يبدأ `temp` على الفرع الحالي؛ تحقق من CI الجديد قبل fast-forward main، وسجّل SHA النهائي في GitHub وNotion. بعدها تابع SYS09-FG01 ثم FG02/FG03/FG04 وفق الاعتماديات، أو عملًا مستقلًا في GL إذا تعذر القرار.

## حدود السلامة

لا تغيّر `muhaseb-erp-analysis/` أو الأسرار أو قاعدة إنتاج؛ لا تكرر ترقية أو مهمة مقبولة، ولا تعلن اكتمال GL14 أو قسم GL. راجع [تفاصيل GL14](gl14-01-foundation.md) و[خطة GL](gl-completion.md).
