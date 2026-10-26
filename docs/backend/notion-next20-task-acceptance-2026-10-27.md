# دفعة تحقق وإكمال 20 مهمة دقيقة إضافية — 27 أكتوبر 2026

**الغرض:** استكمال 20 مهمة جديدة من جدول Notion مختلفة تمامًا عن العشرين المعتمدتين في الجولة السابقة، ضمن GL. **1 مهمة تطوير برمجي جديدة** (GL06-GP03)، **19 مهمة مكتملة التطوير مسبقًا وأعيد تدقيق شروطها**؛ لا يجوز وصف الدفعة بأنها 20 إضافة كود جديدة.

**الدليل:** [GitHub Actions #40412026128](https://github.com/ameenalqershi/ameen-erp/actions/runs/40412026128) على SHA `df924ae9dfe81b8da75a60b06ff371da3117a471`: **281/281 اختبارات ناجحة، صفر فشل**، SQL Server وHTTPS/JWT وtraceability وPROGRESS وOpenAPI. هذا التشغيل شمل المشروع ككل، مع اختبارات محددة جديدة للنطاق النصي. المراجعة ليست اختبارًا مستقلًا لكل مهمة على حدة، بل إثبات ربط تنفيذ ومجموعة اختبارات ذات صلة.

**الكود الجديد:** [temp: GL06-GP03](https://github.com/ameenalqershi/ameen-erp/commit/41190e61988ca6212506524911ef981803cb4c41)؛ [تصحيح بناء المحلل](https://github.com/ameenalqershi/ameen-erp/commit/13d1484e27cd7402402290ad98535bec6bb4541f)؛ [مزامنة مصفوفة التتبع](https://github.com/ameenalqershi/ameen-erp/commit/d93eef7767a72d4484e12eb2473b371b38813823)؛ [تصحيح ملخص PROGRESS](https://github.com/ameenalqershi/ameen-erp/commit/df924ae9dfe81b8da75a60b06ff371da3117a471).

| رقم المهمة | نطاق شرط القبول | الأدلة البرمجية | التصنيف | Notion |
| --- | --- | --- | --- | --- |
| `GL01-A02` | حفظ وتعطيل حساب، تحقق إصدار وأثر وتدقيق | AccountService.EditAccount, SetAccountStatus; AccountTests; AccountSqlIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81b182f9ef725c1e28d4) |
| `GL01-A03` | عرض الاستخدام الحالي وسجل تغييرات دليل الحسابات | GetAccountUsage, ListAccountAudit; AccountTests; SQL/HTTP | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d812087b0ec2727dcfb36) |
| `GL01-C01` | ضبط دورات الشجرة وحظر الرئيسي وحماية البنية المالية | AccountService + ValidateDimensions; AccountTests; SQL | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d8117b1fcd1b94b546ed6) |
| `GL01-T01` | قبول إنشاء شجرة الحسابات والطبيعة المدينة/الدائنة | AccountTests; AccountSqlIntegration; HostedApiIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d8181a696ee7fab51247a) |
| `GL01-T02` | اختبار رفض دورات الشجرة والحركة والتعديل البنيوي المستخدم | AccountTests; AccountSqlIntegration; HostedApiIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81aeb869d6099bfe181e) |
| `GL04-FG03` | سطور القالب: الحساب ومدين ودائن ومركز التكلفة | RecurringJournalLine; RecurringTests; RecurringSqlIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d812298b6c248f51496bd) |
| `GL04-A01` | إنشاء وبحث قالب دوري ضمن الفرع والصلاحيات | RecurringJournalService.List/Create; RecurringTests; SQL/HTTP | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81518af0d6c14dfb8904) |
| `GL04-A02` | حفظ وتعطيل/تفعيل القالب مع منع النسخ الراكدة | RecurringJournalService.Edit/Status; RecurringTests; SQL | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81139309c82f7dc374de) |
| `GL04-A03` | عرض سجل توليد القالب وتغييرات البطاقة | ListRecurringGenerations/Changes, RecurringSchedule; SQL/HTTP | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d812e9827cfb5a09604b0) |
| `GL04-C01` | منع توليد القالب أكثر من مرة للفترة وعدم اعتماده آليًا | GenerateRecurringJournal; RecurringTests; RecurringSqlIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d8184a2eac3e83a93e35b) |
| `GL04-T01` | قبول توليد قيد دوري في صورة مسودة مستقلة | RecurringTests generation; HostedApiIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81408a6bd46f2da645f8) |
| `GL04-T02` | رفض التكرار والحالة المخالفة واستقلال الموافقة | RecurringTests BR06/BR18/BR03; RecurringSqlIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d8149b1fed5421e784722) |
| `GL05-A01` | اختيار نطاق المراجعة والمعاينة قبل التنفيذ | ProcessingQueue/ProcessingPreview; JournalProcessingTests; SQL/HTTP | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d8119888acb64e74ced87) |
| `GL05-A02` | التحقق من الصلاحيات والاتزان والحالة قبل اعتماد القيود | CheckProcessing; JournalProcessingTests; SQL/HTTP | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81a58bd1ec551f483ffd) |
| `GL05-A04` | تنفيذ اعتماد/إعادة القيد وتسجيل نتيجة المستند | ProcessJournals approve/return; JournalProcessingSqlIntegration | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81e3bd0eca3525e83afb) |
| `GL05-C01` | فصل المنشئ عن المعتمد وتوضيح خطأ كل قيد | JournalEntry.Approve, JournalProcessingResult.Rows; Tests/SQL | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81ee9f1ffe55d88513a0) |
| `GL06-A01` | اختيار نطاق الترحيل ومعاينة أثره بما فيه النطاق النصي | ProcessingQueue/Preview + GL06-GP03; App/SQL/HTTP | تحقق من السابق وتكامل الجديد | [صفحة](https://app.notion.com/p/4053830ba34d81feba5fe732cc90b528) |
| `GL06-A02` | التحقق من حالة الترحيل والصلاحيات ومنع التكرار | CheckProcessing/ValidateForPosting; App/SQL/HTTP | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81458879c981d5cccbb4) |
| `GL06-C01` | ذرية كل مستند وإعلان النجاح والفشل منفصلين | ProcessJournals; JournalProcessingSqlIntegration; Replay/Outbox | تحقق من تنفيذ سابق | [صفحة](https://app.notion.com/p/4053830ba34d81e39995e856ef3ed231) |
| `GL06-GP03` | نطاق أرقام نصي آمن مرتبط بالسنة والفروع مع توافق المرشحات القديمة | JournalService.Processing numberRange; Application/SQL/HTTPS tests | تطوير برمجي جديد | [صفحة](https://app.notion.com/p/4053830ba34d81cc833ddb30f5b6effc) |

## منع تضخيم الإنجاز

- **العدد:** GL01 = 5، GL04 = 7، GL05 = 4، GL06 = 4. 20 مهمة دقيقة موثقة. بعد مراجعة متعمقة حوّلت GL04-FG01 من «مكتمل» إلى «قيد التنفيذ» لأن تحليل الحقل GL04-F02 يحدد فرعًا شرطيًا لكن التنفيذ الحالي يفرض BranchId؛ لذلك **لم تُحتسب** ضمن العشرين واستُبدلت بـGL01-T01 الموثقة.
- **الفجوات المتبقية:** GL04-GP01 التوليد المجدول التلقائي؛ GL05-GP01/GP02 المراجع المستقل والحالة المتعددة؛ GL06-GP01/GP02 توجيه مصادر إضافية وعامل Outbox وسجل فشل تقني ورأس دفعة مستقل. وكذلك فجوات GL02 للعملة والطباعة والاستيراد والمطابقة. لا تدعي أي شاشة GL مكتملة بالكامل.
- **لا تغيير للتحليل:** بقيت مجلدات `muhaseb-erp-analysis/` الأصلية كما كانت. مصفوفة 150 شاشة و2759 حقلًا احتفظت بـ0 شاشة كاملة؛ انتقال `GL06-F05` فقط إلى implemented (295 حقلًا implemented، 44 partial) نتيجة العقد الجديد، لا نسبة جاهزية للنظام.
- **مبدأ التسليم:** نجاح CI لا يثبت تفعيل نشر إنتاجي على VPS. التعديلات مرفوعة إلى فرع main للمستودع الخاص؛ اختبار الإنتاج الفعلي والتشغيل مستمران كمراحل منفصلة.

