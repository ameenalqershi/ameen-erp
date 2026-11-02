# نقطة استئناف تطوير AMEEN ERP — 2026-11-02، SYS09 dependency foundation

## الحالة الأساسية التي فُحصت قبل العمل

- المستودع `ameenalqershi/ameen-erp`، `main` المحلي و`origin/main` متطابقان عند `f00510e1ca7f32548872001be504022a7b8ec17e`، شجرة العمل نظيفة، لا أقفال Git ولا عمليات build/git نشطة. GL14-01 السابقة جزئية، وCI الرئيسي السابق `41389554177` ناجح 337/337.
- **الفرع الجاري:** `temp/sys09-approval-route-foundation-20261102`. لا تُعد تنفيذ أو ترحيل SQL 027 أو GL14-01، ولا تفترض أن `main` بقي عند SHA السابق. افحص `git status --short --branch` و`git fetch origin main` وCI أولًا.

## المهمة والملفات

- **المهمة:** `SYS09-FG01/FG02/FG03/FG04` بنية اعتماد ضرورية لـ `GL14-01`، **جزئية غير مقبولة**. لا تعلن اكتمال SYS09 أو GL14.
- **ملفات جديدة:** `backend/src/Ameen.Erp.Domain/Accounting/ApprovalRoute.cs`، `backend/src/Ameen.Erp.Infrastructure/Persistence/Configurations/ApprovalRouteModel.cs`، `backend/database/028_approval_route_foundation.sql`، `backend/tests/Ameen.Erp.Tests/ApprovalRouteFoundationTests.cs`، `ApprovalRouteSqlIntegration.cs`، `docs/backend/sys09-approval-route-foundation.md`.
- **ملفات معدلة:** `ErpDbContext.cs` (`SchemaVersion=28`)، `ErpDbContext.Validation.cs`، `JournalDocumentTypeModel.cs`، `Program.cs`، `SqlIntegration.cs`، `traceability.py`، `docs/backend/PROGRESS.md`، `gl-completion.md`، `gl14-01-foundation.md`، ومصفوفات التتبع المولدة.
- **اختبار محلي:** `python3 backend/scripts/verify.py --dotnet /root/.dotnet/dotnet --artifacts backend/.artifacts/sys09-foundation-20261102` نجح `339/339` دون SQL حي، Release بلا أخطاء، HTTP/JWT وOpenAPI وtraceability. بصمة المصادر `bb4c1dc882438463a9c59ee6866642c8d94ff383f061aeed517f05da7616055c`.
- **اختبار SQL:** ترقية 027→028 مع فشل DDL ثم تراجع وإعادة وتفرد المراحل وFK مركب وعزل، وحارس EF؛ الاختبار مكتوب لكنه يحتاج CI على SQL Server معزول. لا تدّع نجاحه حتى نتيجة CI.
- **SHA/CI الجديد:** لم يثبتا بعد عند كتابة هذا السجل. استخرج SHA من `git rev-parse HEAD` بعد commit `temp:` وراجع CI على SHA نفسه. إذا نجح، حدّث `verification-results.json` من artifact الصحيح، ثم commit توثيق `temp:`، تحقق CI النهائي قبل fast-forward إلى `main`، وحدّث Notion. إذا فشل CI، أصلح على نفس الفرع ولا تدفع إلى main.

## المانع وخطوة الاستئناف الدقيقة

- `SYS07` لا يملك كتالوج أدوار دائمًا؛ `ApproverRoleId` لا يمكن التحقق منه. قيم `DocumentType` محددة بسياسة لم تُعتمد بعد، وSYS09 يمنع تداخل المسارات حسب المستند والنطاق والمبلغ. لا تفترض نوع مستند أو سياسة عملة أو فرع. لذلك **كل حفظ SYS09/GL14 محجوب**؛ لم يُفتح API أو اعتماد متعدد المراحل أو تصعيد مهلة.
- **التالي:** بعد قبول CI 028 وتحديث الأدلة، ابدأ SYS07 role catalog وفق `muhaseb-erp-analysis` والاعتماديات؛ ثم FK الدور والتحقق من نطاقه والصلاحيات ومنع التعارض ضمن معاملة، ثم إتاحة SYS09 وGL14 تدريجيًا باختبارات قبول موجبة وسالبة. أبقِ `GL14-01` في Notion قيد التنفيذ.
- لا تمس أسرارًا أو قواعد إنتاج أو ملفات `muhaseb-erp-analysis/`؛ ولا ترفع فشلًا إلى `main`.
