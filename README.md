# Ameen ERP

مشروع نظام ERP محاسبي لليمن والخليج.

المرحلة الحالية: بدأ تنفيذ الباك إند باستخدام ASP.NET Core APIs على .NET 10 وSQL Server، مع نواة تهيئة وأستاذ عام متعددة الشركات والفروع والعملات. الواجهة المستقبلية المقترحة Flutter للويب والموبايل؛ التركيز الحالي على الباك إند.

## التنفيذ الحالي

أُضيفت الحسابات والفترات والعملات وأسعار الصرف ومراكز التكلفة وعضويات الصلاحيات، ودورة القيد اليدوي من المسودة إلى الاعتماد والترحيل والعكس، وسجل التدقيق وOutbox ومنع تكرار الأوامر، وميزان مراجعة بمستويات الدليل ومرشح المركز وإخفاء الصفري، وأستاذ بمرشح المركز والمرجع الخارجي.

- [تشغيل وبناء واختبار الباك إند](backend/README.md).
- [البنية والقرارات التقنية](docs/backend/architecture.md).
- [عقود API وأمثلة دورة القيد](docs/backend/api.md).
- [المطابقة التفصيلية مع المخططات والحقول](docs/backend/traceability.md).
- [نتيجة التحقق وحدوده](docs/backend/validation.md).

هذه نواة تنفيذ تشمل أجزاء من 21 شاشة ووظيفة؛ بقية متطلبات الشاشات الـ150 موثقة كعمل متبقٍ. لم يُعتمد النظام للإنتاج ولم تكتمل جميع الأقسام.

## التحليل

نُقل مجلد التحليل كاملًا إلى [muhaseb-erp-analysis](muhaseb-erp-analysis/README.md) مع التحقق من بقاء محتوى ملفاته دون تغيير.

- [التقرير المرئي القابل للبحث](muhaseb-erp-analysis/index.html) — يفتح محليًا في المتصفح.
- [أطلس المخططات PDF](muhaseb-erp-analysis/diagrams-atlas.pdf).
- [نطاق التحليل والقرارات](muhaseb-erp-analysis/docs/01-scope-and-decisions.md).
- [سجل مطابقة المتكامل](muhaseb-erp-analysis/docs/06-reference-parity.md).
- [مراجعة جودة الحزمة](muhaseb-erp-analysis/QUALITY.md).

الحزمة تشمل 15 قسمًا، و150 شاشة ووظيفة، و28 مخططًا، و6 نماذج تخطيطية. المطابقة التفصيلية مع إصدار محدد من المتكامل لم تثبت بعد، وحدودها موضحة في سجل المطابقة.

## هيكل المشروع

```text
ameen-erp/
├── README.md
├── .gitignore
├── backend/
│   ├── src/                 # Domain / Application / Infrastructure / Api
│   ├── tests/               # اختبارات أعمال وتكامل SQL اختيارية
│   ├── database/            # سكربت إصدار قاعدة البيانات ودور التشغيل
│   └── scripts/             # توليد وفحص المطابقة
├── docs/backend/           # العقود والقرارات ومصفوفات التنفيذ
├── .github/workflows/      # التحقق باستخدام .NET 10 وSQL Server
└── muhaseb-erp-analysis/
    ├── docs/
    ├── data/
    ├── diagrams/
    ├── wireframes/
    ├── scripts/
    ├── index.html
    └── diagrams-atlas.pdf
```

## إعادة التحقق

```bash
cd muhaseb-erp-analysis
python3 scripts/validate.py
node scripts/check_report_logic.cjs
```

تعليمات توليد الملفات في دليل مجلد التحليل.

فحص مطابقة التنفيذ دون تغيير التحليل: `python3 backend/scripts/traceability.py --check`.

## إعداد المستودع المطلوب

الحساب المستهدف: `ameenalqershi`.
اسم المستودع المقترح: `ameen-erp`.
إعداد GitHub المطلوب: **Private**.

هذه بيانات إعداد مستهدفة، وليست تأكيدًا بإنشاء مستودع GitHub. لا تحفظ كلمات المرور أو رموز الوصول ضمن المشروع.
