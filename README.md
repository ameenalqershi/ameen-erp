# Ameen ERP

مشروع نظام ERP محاسبي لليمن والخليج.

المرحلة الحالية: التحليل الوظيفي والمخططات قبل تنفيذ التطبيق.

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

## إعداد المستودع المطلوب

الحساب المستهدف: `ameenalqershi`.
اسم المستودع المقترح: `ameen-erp`.
إعداد GitHub المطلوب: **Private**.

هذه بيانات إعداد مستهدفة، وليست تأكيدًا بإنشاء مستودع GitHub. لا تحفظ كلمات المرور أو رموز الوصول ضمن المشروع.
