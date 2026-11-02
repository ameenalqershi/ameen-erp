# نقطة استئناف تطوير AMEEN ERP — 2026-11-02

## الحالة المثبتة

- المستودع: `ameenalqershi/ameen-erp`، الفرع المستهدف `main`، آخر كود مقبول `713810cdb4e933e824162048aedbd2dea91ce14b` (`temp: verify GL02 positive opening acceptance lifecycle`). فرع العمل المؤقت `temp/gl02-t01-positive-acceptance-20261102`، وقد رُفع الكود نفسه إلى main بتقدم سريع.
- المهمة `GL02-T01`: مكتملة ومحدثة في Notion؛ اختبار إيجابي في `backend/tests/Ameen.Erp.Tests/GeneralOpeningTests.cs`. لا حاجة لإعادة تنفيذها.
- التحقق: محلي 333/333 دون SQL؛ [CI #41383316448](https://github.com/ameenalqershi/ameen-erp/actions/runs/41383316448) على SHA المذكور 334/334، SQL Server معزول وHTTP/JWT وOpenAPI وRelease. بصمة المصادر `34435d59b0910ac3bf4a66d92115e2a631be4df3b1a5417363e9248027fddc0f`.
- التقرير المعتمد: `docs/backend/verification-results.json`، والمصفوفات في `docs/backend/*-implementation.csv`، وقاعدة توليدها `backend/scripts/traceability.py`. `PROGRESS.md` يعاد توليد مقطعه الآلي عبر `backend/scripts/progress.py`.
- لا يوجد مانع مثبت في GL02-T01. لم تتغير ملفات `muhaseb-erp-analysis` الأصلية. لا تلمس الإنتاج أو الأسرار.

## الاستئناف التالي

1. قبل التغيير: `git fetch origin main`، افحص `git status --short` و`git branch --show-current` و`git ls-remote origin refs/heads/main`، والجلسات والعمليات النشطة، وأحدث CI وNotion؛ لا تكرر قبول GL02-T01.
2. داخل GL02 افحص الاعتماديات المفتوحة: GL02-A07/GP02 طباعة تتطلب SYS12؛ GP03 استيراد يتطلب SYS13؛ C01/GP04 بوابة التصفير الشاملة تتطلب سياسة مطابقة واعتماديات INV06/SAL03/PUR03؛ GP05 دفاتر مساعدة غير منفذة. لا تختلق سياسة قطع أو تصفير ولا تعلن اكتمال الشاشة.
3. اختر أصغر جزء مستقل موثق من GL وفق `docs/backend/gl-completion.md` وNotion؛ ابدأ بالمهمة المقطوعة إن ظهرت. أنشئ فرعًا يبدأ `temp`، نفذ الكود والاختبارات، ثم ارفع إلى main فقط بعد نجاح التحقق، وسجل SHA وCI وNotion والمصفوفات.
4. إذا كان commit توثيق التحقق أحدث من SHA الكود أعلاه، تأكد من نجاح CI عليه قبل اعتبار تقرير التقدم الحالي معتمدًا؛ لا تعِد إجراء ترحيل SQL أو تنفيذ اختبار على بيانات إنتاج.
