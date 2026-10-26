# اعتماد عشرين مهمة دقيقة من Notion — 27 أكتوبر 2026

**النطاق:** الأستاذ العام GL فقط، وفق حزمة التحليل الأصلية غير المعدلة. شملت الجولة 20 مهمة دقيقة مستقلة: GL01=5، GL02=5، GL03=10. واحدة هي **GL03-A01** أضيف تنفيذها برمجيًا في هذه الجولة؛ **19** كانت منفذة من قبل وتم تدقيقها وإغلاق حالة Notion بعد تحقق قبولها. لا يجوز وصف العشرين بأنها عشرون ميزة جديدة أو عشرون شاشة مكتملة.

**دليل تنفيذ الجولة:** [commit وظيفي 90437e2](https://github.com/ameenalqershi/ameen-erp/commit/90437e2612f345064487edd311371b2e9347bed3) مع [تصحيح اختبار البناء 46afe78](https://github.com/ameenalqershi/ameen-erp/commit/46afe78e98dcbf4dfb02b7eabfd04444d28e4c2b). الاختبار النهائي لنفس كود 46afe78: [GitHub Actions #40409584891](https://github.com/ameenalqershi/ameen-erp/actions/runs/40409584891) = **280/280 ناجحة، 0 فشل**، مع SQL Server وHTTPS/JWT وOpenAPI وتدقيق آثار التحليل. التشغيل المبدئي للـcommit 90437e2 فشل في بناء اختبار HTTP بسبب nullable receipt.Number، وتلاه التصحيح وإعادة النجاح؛ لا يطمس سجل الفشل الأول.

| المهمة | النطاق المعتمد | أدلة التنفيذ والاختبار | طبيعة الإنجاز | Notion |
|---|---|---|---|---|
| `GL01-FG01` | حقول المعرف والشركة والرمز والاسم العربي | Domain/Accounting/Account.cs; AccountService; AccountTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81658a28ca6cf3c5c185) |
| `GL01-FG02` | الاسم الإنجليزي والنشاط والملاحظات والأب | AccountService; AccountTests; AccountSqlIntegration | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d8135a191e0110f769e81) |
| `GL01-FG03` | تصنيف الحساب ونوعه وطبيعته وعملته | AccountService; AccountTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d815eb166dea08a377b9b) |
| `GL01-FG04` | الرقابة ومركز التكلفة الإلزامي | AccountService; JournalService.ValidateDimensions; AccountTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d818c9a08dd88ff3a7cb1) |
| `GL01-A01` | بحث وإنشاء بطاقة حساب | AccountsEndpoints; AccountService; AccountSqlIntegration | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81f49c25c79e076f509c) |
| `GL02-FG01` | معرف افتتاح وشركة وفرع وسنة مشتقة | JournalService.GeneralOpening; JournalEntry; GeneralOpeningTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d8170bedcf083f4b83910) |
| `GL02-FG04` | تاريخ التشغيل ومرجع الميزان السابق | JournalService.GeneralOpening; DraftJournalHeaderTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d810683cdd9ed30adb1df) |
| `GL02-FG05` | الحساب والمدين والدائن والمركز | JournalService.PrepareLines; JournalLine; GeneralOpeningTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81bb9e15e73da5d2a3c5) |
| `GL02-FG06` | إنشاء وتاريخ وإصدار وتعديل افتتاح | JournalEntry; GeneralOpeningReversalTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81eb9d56f4a46b12f71a) |
| `GL02-A06` | ترحيل افتتاح من المصدر المصرح | JournalService.TransitionJournal; JournalProcessing; SQL integration | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d814bbeb5d7fe55e7583e) |
| `GL03-FG01` | هوية القيد وشركته وفرعه والسنة | JournalService.CreateJournal; DraftJournalHeaderTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81788506d8a6ce9da4fd) |
| `GL03-FG05` | الحساب ومدين ودائن وعملة السطر | JournalLine; PrepareLines; RateAndCloseTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d814baffec38c98713905) |
| `GL03-FG07` | بيان سطر القيد | JournalLine.Description; JournalProcessingTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81dfa2a1f6a7ec2a9efc) |
| `GL03-A01` | بحث وإنشاء قيد، مرشحات الرقم والوصف والمرجع | JournalsEndpoints; JournalService.ListJournals; JournalProcessingTests GL03-A01; HostedApiIntegration | تطوير جديد | [فتح](https://app.notion.com/p/4053830ba34d81d581dbcf60317a868b) |
| `GL03-A02` | تعديل وحفظ المسودة مع تحقق الرأس والسطور | JournalService.EditJournal; DraftJournalHeaderTests | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d812f9282e915c509f300) |
| `GL03-A03` | تقديم القيد والتحقق من الاتزان | JournalEntry.Submit; JournalService.TransitionJournal; AT01 | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d81999754f5832cd058c7) |
| `GL03-A04` | اعتماد القيد مع فصل الصلاحيات | JournalEntry.Approve; JournalProcessingTests; SQL | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d817686ecc5e43be88ea5) |
| `GL03-A06` | ترحيل القيد الذري حسب المصدر | JournalService.TransitionJournal; Outbox; SQL/HTTPS | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d812a8741e6ec5dd44cac) |
| `GL03-A08` | عكس مرتبط يحافظ على القيم التاريخية | JournalService.ReverseJournal; JournalLine.Opposite; SQL | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d816fb8dcee011c9dee87) |
| `GL03-C01` | طرف واحد موجب وحظر الرقابة غير المصرحة | JournalLine; JournalService.ValidateDimensions; BR01/BR08 | سابق متحقق | [فتح](https://app.notion.com/p/4053830ba34d816086c4fd6292d221d5) |

## حدود هذه الشهادة

- يشير «مكتمل» إلى **المهمة الدقيقة ذات الحقول أو الفعل المحدد** فقط بعد التحقق من كودها وأدلة تشغيل المجموعة؛ ولا يعني أن شاشة GL01 أو GL02 أو GL03 اجتازت بوابة التغطية النهائية.
- لم يكتمل GL02-GP01 (عملة الافتتاح الأجنبية)، GL02-GP02 (SYS12 طباعة)، GL02-GP03 (SYS13 استيراد)، GL02-GP04 (سياسة قطع/تصفير)، GL02-GP05 (SAL03/PUR03 والدفاتر المساعدة). لا تغيير لوضعها المحجوب أو المفتوح.
- GL03 لا تزال تنقصها أنواع القيود، المرفقات، الطرف/المشروع، العكس المجدول والطباعة. GL01 يحتاج مسار تحويل بنية الحساب المستخدم. لا تتغير مصفوفة «صفر شاشة مكتملة».
- ملفات `muhaseb-erp-analysis/` المرجعية ومخططاتها بقيت بلا أي تعديل. لا اختراع لسياسة مالية أو مواعيد إنجاز.

