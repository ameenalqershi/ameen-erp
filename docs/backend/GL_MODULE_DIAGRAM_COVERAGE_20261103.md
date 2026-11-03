# GL module navigation diagram and implementation audit — 2026-11-03

**Notion deliverable:** `DIAG-module-GL` — audit the original Mermaid and SVG screen map against approved GL requirements and today's implementation. This is **diagram coverage verification**, not an ERP-ready declaration.

**Baseline:** `gl@2508bb5e32181a6c8525969af64df0a63a1a473d`. Source: `muhaseb-erp-analysis/diagrams/source/module-GL.mmd` and `diagrams/svg/module-GL.svg`; requirements: `muhaseb-erp-analysis/docs/modules/GL.md`, `data/screens.json` and `data/fields.csv`. The read-only audit does not modify these files.

## Structural results

- **14 GL screen nodes** `GL01` through `GL14`, all present exactly once in original Mermaid, original SVG, GL.md section headings, analysis/screens.json, and `screens-implementation.csv`.
- **4 navigation categories**: `cat0` البيانات الأساسية → `GL01`, `cat1` العمليات → `GL02`, `cat2` التهيئة → `GL04`, `cat3` الإجراءات → `GL05`.
- **18 directed navigation links**: 4 from root to categories, 4 category-to-screen entries and 10 screen-to-screen arrows. No unknown endpoints or missing GL screen definitions.
- Source-to-source navigation links: `GL02 → GL03`, `GL04 → GL13`, `GL05 → GL06`, `GL03 → GL07`, `GL13 → GL14`, `GL06 → GL08`, `GL07 → GL09`, `GL08 → GL10`, `GL10 → GL11`, `GL11 → GL12`.
- **CRITICAL:** The original SVG caption says the arrows show **navigation order within the category**, **not a mandatory accounting sequence**. Never use a Mermaid edge as a posting, approval, reconciliation or period-close dependency. Such gates must come from the GL.md rules and BR/AT specifications.

## Screen and field coverage (implementation inventory, not proof of full completion)

| Screen | Inventory state | Field states | Known remaining work | Representative test file |
|---|---|---|---|---|
| GL01 | partial | audit 3, implemented 15 | مسار تحويل معتمد لتغيير بنية الحساب المستخدم؛ سجل الاستخدام الحالي للأستاذ والقوالب فقط حتى تنفيذ الدفاتر المساعدة | `backend/tests/Ameen.Erp.Tests/AccountTests.cs` |
| GL02 | partial | derived 1, implemented 21 | الطباعة متاحة كصفحة RTL HTML للمتصفح فقط؛ تصدير PDF/اختيار قالب رسمي غير معتمد؛ استيراد الافتتاح وتصفير حساب التسوية بوابة شاملة غير منفذ؛ الذمم والمصادر غير المنفذة ليست مطابقة | `backend/tests/Ameen.Erp.Tests/GeneralOpeningTests.cs` |
| GL03 | partial | derived 1, implemented 22, partial 3, pending 2 | المشروع والطرف والمرفقات وأنواع القيود GL14؛ تفعيل عامل العكس المجدول في بيئة النشر مشروط بإعداد ActorSubject المفوض؛ لا يعد توليد مسودة العكس موافقة أو ترحيلًا | `backend/tests/Ameen.Erp.Tests/JournalAuditTests.cs` |
| GL04 | partial | implemented 15, partial 1 | الفرع في المخطط شرطي لكن العقد الحالي يتطلبه (GL04-F02)؛ خيارات دورية أخرى غير معتمدة؛ يلزم تفعيل المشغل على الإنتاج | `backend/tests/Ameen.Erp.Tests/RecurringTests.cs` |
| GL05 | partial | audit 3, implemented 9, pending 1 | مراجع مستقل عن المعتمد ومراحل اعتماد متعددة؛ حالة مراجعة مستقلة عن حالة القيد وإصدار عددي للإجراء | `backend/tests/Ameen.Erp.Tests/JournalProcessingTests.cs` |
| GL06 | partial | implemented 12, partial 1 | توجيه مستندات المصادر الأخرى وعامل Outbox وتشغيله في الإنتاج؛ ربط سياسة مسارات الاعتماد متعددة المراحل مع SYS09 غير مكتمل؛ ملف الدولة والمصادر الخارجية لم تُنفذ | `backend/tests/Ameen.Erp.Tests/JournalProcessingTests.cs` |
| GL07 | partial | derived 1, implemented 16, partial 2 | عكس مستندات الدفاتر المساعدة وفحص التبعيات والضريبة؛ مشغل العكس اختياري وغير مثبت التفعيل في الإنتاج؛ الأنواع غير اليدوية تظل تعكس من مستند المصدر | `backend/tests/Ameen.Erp.Tests/JournalAuditTests.cs` |
| GL08 | not-started | pending 15 | الشاشة كاملة | `لا يوجد` |
| GL09 | not-started | pending 25 | الشاشة كاملة | `لا يوجد` |
| GL10 | not-started | pending 14 | الشاشة كاملة | `لا يوجد` |
| GL11 | partial | audit 3, implemented 2, partial 3, pending 5 | إقفال ERP شامل ومطابقة بنوك وذمم ومخزون وضريبة وأصول وقيد نتائج السنة وإصدار عددي للإجراء | `backend/tests/Ameen.Erp.Tests/RateAndCloseTests.cs` |
| GL12 | partial | audit 2, implemented 4, pending 5 | مرجع موافقة مستقل ونافذة فتح مؤقتة وإعادة إقفال تلقائي، وفحوص إقرارات SYS14/الذمم عند إعادة الفتح، لم تنفذ؛ لا يعتبر سجل الفتح شهادة بهذه الموافقات | `backend/tests/Ameen.Erp.Tests/RateAndCloseTests.cs` |
| GL13 | partial | implemented 17 | GL13 خريطة ومراجعة لا تنفيذ تقارير RPT03/RPT04 كاملة؛ النطاق الاختياري للفرع مرشح للأستاذ ويتطلب تفويض جميع الفروع لدليل الحسابات؛ ParentCode للعرض فقط والتجميع بمعادلة صريحة؛ لا لغة ضرب/قسمة أو قواعد تدفق نقدي مفترضة | `backend/tests/Ameen.Erp.Tests/FinancialMappingTests.cs` |
| GL14 | partial | partial 13 | SYS09 route schema and GL14 tenant FK exist but roles, route activation and policy validation are blocked; create/edit/disable/search/usage/audit APIs, GL03 binding, runtime enforcement of manual/automatic origin and reversal separation, and full acceptance remain unimplemented | `backend/tests/Ameen.Erp.Tests/JournalDocumentTypeFoundationTests.cs` |

There are **11 partial GL screens and 3 not-started screens** (GL08, GL09, GL10). GL13 has mapped fields but remains partial as it maps/reviews statement definitions; RPT03/RPT04 report generation is a separate deliverable. A field shown as `audit` or `derived` is not assumed to be a direct editable API/SQL column; `partial` and `pending` are not counted as implemented.

## Risk and dependency mapping

1. **GL01 → GL02 → GL03:** account classification and source-specific openings must respect company/branch membership, historical FX, control-account exclusion, balanced lines and idempotent processing. The chart's `GL02 → GL03` edge is navigation only; approved workflow constraints are in GL.md.
2. **GL04/GL05/GL06:** templates generate drafts without automatic approval; review/approval and posting enforce independent permissions, immutable audit and per-document atomicity. Independent multistage SYS09 reviewer policy and an Outbox worker are not complete.
3. **GL07:** manual and general-opening linked reversals preserve historical amounts. INV06 must reverse from its source, and recurring/SAL/PUR reversal policies or dependencies cannot be guessed. The optional scheduled runner does not auto-approve or post.
4. **GL08/09/10:** do not create schema/enum without approved rate type, scheduling type or subsidiary-ledger type. Their source and financial dependencies (SYS05, SAL/PUR, RPT06/RPT08) remain blockers.
5. **GL11/12:** existing GL-only close and scoped reopen history are not a whole-ERP closing or approved temporary reopening workflow. GL10, tax declarations, subsidiary reconciliation, approval reference and reclose window are unresolved.
6. **GL13/14:** financial mapping CRUD/review is tested; document-type activation is still blocked by SYS07/SYS09 identity, role and route enforcement. Do not promote internal FK foundation to approved GL14 API or running policy.

## Reproducible audit and CI gates

- Programmatically matched all 14 Mermaid nodes against `screens.json`, `GL.md`, the SVG text and the implementation matrix; validated all 18 arrow endpoints and 14 entry/group links on a clean isolated temp worktree.
- `python3 backend/scripts/traceability.py --check` validates original analysis manifest and deterministic implementation matrices (150 screens, 2759 fields, 28 diagrams, 20 rules).
- `python3 backend/scripts/progress.py --check` validates handoff inventory without declaring complete screens.
- The GL baseline has SQL Server/HTTPS/JWT/OpenAPI coverage; see prior successful `gl` CI #41585860525 (404/404), plus GL07-C01 branch CI #41586782683 (404/404). This audit PR's own exact-head CI must pass before closing DIAG-module-GL.
- Scope limitation: a navigation diagram alone **cannot** establish every end-to-end source workflow, source-of-truth, FK, tax policy, or financial approval. Its complete **coverage audit** does not imply complete **implementation of GL**.

No source diagram was edited, no production data was accessed, and no new accounting policy was introduced.
