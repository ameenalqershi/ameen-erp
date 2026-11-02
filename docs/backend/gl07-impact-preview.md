# GL07-A05 — معاينة الأثر التاريخي لقيد العكس

تحقيق مستقل لفعل «معاينة الأثر» في شاشة العكس GL07، بالاعتماد على عقد `GL03-A05` القائم دون إنشاء API مكرر.

- `GET /api/v1/companies/{companyId}/journals/{id}/impact-preview` يعرض مسودة `JournalSource.Reversal` بعد فحص العضوية ونطاق الفرع.
- يقرأ السطور والمبالغ المحوّلة المحفوظة تاريخيًا، مع عكس كل طرف `Debit/Credit` و`FunctionalDebit/FunctionalCredit` دون تطبيق أسعار صرف جديدة أو تغيير لحسابات المصدر.
- يظهر فرق الاتزان وموانع التقديم كما في تحقق الأستاذ؛ لا يعتمد أو يرحل أو ينشئ `AuditEvent` أو `OutboxEvent` أو مستندًا إضافيًا.
- مخطط GL07 الأصلي يوجب العكس عبر مستند المصدر للمستندات الآلية؛ هذه المعاينة لا تسمح بتجاوز ذلك الضابط.
- الاختبارات: `JournalImpactPreviewTests.cs` للانعكاس الحرفي وعدم الكتابة ونطاق الفرع، `HttpIntegration.cs` لمسودة العكس، `ScheduledReversalHostedIntegration.cs` لعقد HTTPS/JWT/SQL الحقيقي، و`OpenApiSnapshotTests.cs` لتأمين المسار.
- يلزم نجاح فحوص `traceability.py` و`progress.py` والبناء وCI SQL على SHA الدفعة. لا تدل هذه المهمة على اكتمال GL07 أو الدفاتر المساعدة أو الطباعة أو تفعيل المجدول بالإنتاج.
