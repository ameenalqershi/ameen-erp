# عقود API v1

كل المسارات المالية تحت `/api/v1` وتتطلب `Authorization: Bearer <access_token>`. وصف OpenAPI في `/openapi/v1.json` يحتاج توكن أيضًا. يُولد من مسارات الكود مع أنواع استجابة صريحة، ووصف Bearer ورأس Idempotency-Key والمبالغ النصية والتواريخ. جرى اختباره فعليًا في مضيف .NET 10؛ [نسخة مولّدة](../../backend/openapi/v1.json) متاحة للمراجعة وتوليد عملاء API.

كل `POST` و`PUT` يتطلب رأس `Idempotency-Key` بطول 1–100. أنشئ UUID جديدًا للعملية المنطقية الواحدة وأعد **المفتاح والمحتوى نفسيهما** عند انقطاع الشبكة. لا تستخدم مفتاحًا واحدًا لعمليتين مختلفتين. التعديل والانتقالات وتعطيل السجلات تتطلب `expectedVersion` من آخر قراءة. لا تعالج `version_conflict` بإعادة إرسال الإصدار القديم؛ اقرأ السجل واعرض التعارض ثم أرسل أمرًا جديدًا بمفتاح جديد.

المبالغ والأسعار في الاستجابة نصوص عشرية، مثل `"100.25"` و`"3.75"`. المدخلات تقبل النص الدقيق أو رقم JSON القابل للتمثيل الدقيق؛ يوصى دائمًا بالنص. لا فواصل آلاف ولا صيغة أسية. التواريخ `yyyy-MM-dd` والأوقات UTC بصيغة ISO. الأعداد الصحيحة مثل حجم الصفحة ومقياس العملة ليست نصوص مبالغ. أسماء الحالات والتصنيفات نصية، لا أرقام enum.

## الفهرس

في الجدول `C = /api/v1/companies/{companyId}`. كل مسار أقصر في عمود الإجراء نسبي إلى C، إلا قائمة وإنشاء الشركات.

| العملية | المسار | نوع الطلب/الاستجابة | الصلاحية |
|---|---|---|---|
| GET | `/api/v1/companies` | قائمة الشركات ذات العضوية المفعلة | توثيق الهوية |
| POST | `/api/v1/companies` | CreateCompanyRequest → CommandReceipt | erp.platform-admin |
| GET / PUT | C | Company / EditCompanyRequest | ledger.read / company.configure لكل الفروع |
| POST | `/status` | SetCompanyStatusRequest | company.configure لكل الفروع |
| GET | `/usage` | CompanyUsage | ledger.read لكل الفروع |
| GET | `/profile-audit-events` | Page&lt;AuditEvent&gt; | read + audit.read لكل الفروع |
| GET / POST | `/branches` | قائمة / CreateBranchRequest | read / company.configure |
| GET | `/branches/search` | Page&lt;Branch&gt;؛ search/parentId/isActive | ledger.read بنطاق الفروع |
| GET / PUT | `/branches/{id}` | Branch / EditBranchRequest | read / company.configure لكل الفروع |
| POST | `/branches/{id}/status` | SetBranchStatusRequest | company.configure لكل الفروع |
| GET | `/branches/{id}/usage` | BranchUsage | ledger.read لكل الفروع |
| GET | `/branches/{id}/audit-events` | Page&lt;AuditEvent&gt; | read + audit.read لكل الفروع |
| GET / POST | `/items` | Page&lt;Item&gt; / CreateItemRequest | ledger.read / company.configure لكل الفروع |
| GET / PUT | `/items/{id}` | Item / EditItemRequest | ledger.read / company.configure لكل الفروع |
| POST | `/items/{id}/status` | SetItemStatusRequest | company.configure لكل الفروع |
| GET | `/items/{id}/audit-events` | Page&lt;AuditEvent&gt; | ledger.read + audit.read لكل الفروع |
| GET | `/items/{id}/convert-quantity` | ItemQuantityConversion؛ unitId وquantity إلزاميان | ledger.read |
| GET | `/units/{id}/items` | Page&lt;Item&gt; | ledger.read لكل الفروع |
| GET / POST | `/item-groups` | Page&lt;ItemGroup&gt; / CreateItemGroupRequest | ledger.read / company.configure لكل الفروع |
| GET / PUT | `/item-groups/{id}` | ItemGroup / EditItemGroupRequest | ledger.read / company.configure لكل الفروع |
| POST | `/item-groups/{id}/status` | SetItemGroupStatusRequest | company.configure لكل الفروع |
| GET | `/item-groups/{id}/audit-events` | Page&lt;AuditEvent&gt; | ledger.read + audit.read لكل الفروع |
| GET | `/accounts/{id}/item-groups` | Page&lt;ItemGroup&gt; | ledger.read لكل الفروع |
| GET / POST | `/units` | Page&lt;UnitOfMeasure&gt; / CreateUnitRequest | ledger.read / company.configure لكل الفروع |
| GET / PUT | `/units/{id}` | UnitOfMeasure / EditUnitRequest | ledger.read / company.configure لكل الفروع |
| POST | `/units/{id}/status` | SetUnitStatusRequest | company.configure لكل الفروع |
| GET | `/units/{id}/audit-events` | Page&lt;AuditEvent&gt; | ledger.read + audit.read لكل الفروع |
| GET / POST | `/currencies` | قائمة / CreateCurrencyRequest | read / company.configure |
| GET | `/currencies/search` | Page&lt;Currency&gt;؛ search/isActive | ledger.read |
| GET / PUT | `/currencies/{id}` | Currency / EditCurrencyRequest | read / company.configure |
| POST | `/currencies/{id}/status` | SetCurrencyStatusRequest | company.configure |
| GET | `/currencies/{id}/usage` | CurrencyUsage | ledger.read، كل الفروع |
| GET | `/currencies/{id}/audit-events` | Page&lt;AuditEvent&gt; | read + audit.read، كل الفروع |
| GET / POST | `/exchange-rates` | قائمة / CreateExchangeRateRequest | read / company.configure |
| GET | `/exchange-rates/search` | Page&lt;ExchangeRate&gt;؛ currencyCode/from/to/source/offset/limit | ledger.read |
| GET | `/exchange-rates/quote` | ExchangeRateQuote؛ currencyCode وdate إلزاميان | ledger.read |
| GET | `/exchange-rates/{id}` | ExchangeRateDetail؛ السعر والعملة الوظيفية | ledger.read |
| GET | `/exchange-rates/{id}/usage` | Page&lt;AccountUsageReference&gt; | ledger.read لكل الفروع |
| GET | `/exchange-rates/{id}/audit-events` | Page&lt;AuditEvent&gt; | read + audit.read لكل الفروع |
| GET / POST | `/fiscal-years` | قائمة / CreateFiscalYearRequest | read / company.configure |
| GET | `/fiscal-years/search` | Page&lt;FiscalYear&gt;؛ search/offset/limit | ledger.read |
| GET | `/fiscal-years/{id}` | FiscalCalendar؛ السنة والفترات وحالة إقفال الأستاذ | ledger.read |
| GET | `/fiscal-years/{id}/usage` | FiscalYearUsage | ledger.read لكل الفروع |
| GET | `/fiscal-years/{id}/audit-events` | Page&lt;AuditEvent&gt; | read + audit.read لكل الفروع |
| GET | `/periods` | قائمة الفترات | ledger.read |
| GET | `/periods/{id}/close-preview` | PeriodClosePreview؛ checks وissues بصفحات | period.close + ledger.read لكل الفروع |
| POST | `/periods/{id}/close` | ChangePeriodRequest | period.close |
| POST | `/periods/{id}/reopen` | ChangePeriodRequest | period.reopen |
| GET / POST | `/cost-centers` | قائمة / CreateCostCenterRequest | read / company.configure |
| GET | `/cost-centers/search` | Page&lt;CostCenter&gt;؛ search/branchId/parentId/isActive | ledger.read بنطاق الفروع |
| GET / PUT | `/cost-centers/{id}` | CostCenter / EditCostCenterRequest | read / company.configure |
| POST | `/cost-centers/{id}/status` | SetCostCenterStatusRequest | company.configure |
| GET | `/cost-centers/{id}/usage` | CostCenterUsage | ledger.read، كل الفروع |
| GET | `/cost-centers/{id}/audit-events` | Page&lt;AuditEvent&gt; | read + audit.read، كل الفروع |
| GET / POST | `/memberships` | Page / GrantMembershipRequest | members.manage |
| POST | `/memberships/{id}/status` | SetMembershipStatusRequest | members.manage |
| GET / POST | `/accounts` | Page / CreateAccountRequest | read / company.configure |
| GET / PUT | `/accounts/{id}` | Account / EditAccountRequest | read / company.configure (كل الفروع للتعديل) |
| GET | `/accounts/{id}/usage` | AccountUsage مع مراجع مقسمة | ledger.read، كل الفروع |
| GET | `/accounts/{id}/audit-events` | Page&lt;AuditEvent&gt; | read + audit.read، كل الفروع |
| POST | `/accounts/{id}/status` | SetAccountStatusRequest | company.configure |
| GET | `/journals` | Page&lt;JournalSummary&gt; | ledger.read |
| GET | `/journals/{id}` | JournalEntry مع السطور | ledger.read |
| POST | `/journals` | CreateJournalRequest | journal.write |
| PUT | `/journals/{id}` | EditJournalRequest | journal.write |
| POST | `/journals/{id}/submit` | TransitionRequest | journal.write |
| POST | `/journals/{id}/approve` | TransitionRequest | journal.approve |
| POST | `/journals/{id}/return` | TransitionRequest وسبب إلزامي | journal.approve |
| POST | `/journals/{id}/post` | TransitionRequest | journal.post |
| POST | `/journals/{id}/cancel` | TransitionRequest وسبب إلزامي | journal.write |
| POST | `/journals/{id}/reverse` | ReverseJournalRequest | journal.reverse |
| GET | `/reports/trial-balance` | TrialBalanceResult | ledger.read |
| GET | `/reports/accounts/{accountId}/ledger` | AccountLedgerResult | ledger.read |
| GET | `/audit-events` | Page&lt;AuditEvent&gt; | audit.read |

كل أوامر الإعداد والعضويات والإقفال وقراءة التدقيق تتطلب صلاحية على جميع فروع الشركة. القراءة المالية تحترم الفروع المصرح بها، وتمرير `branchId` غير مسموح ينتج 403. قائمة الشركات تكشف فقط الشركات ذات عضوية فعالة؛ لا تمنح صلاحية مالية بمجرد ظهور الشركة.

التقسيم إلى صفحات: `offset=0&limit=50`، الحد الأقصى 200، والإزاحة حتى مليون. قوائم الحسابات تدعم `search`، وقائمة القيود تدعم `branchId` و`state`. التقارير تتطلب `from` و`to` وتقبل `branchId`. الأستاذ يدعم pagination مع احتساب الجاري من بداية الفترة، لا من بداية الصفحة. صفحة العدد والعناصر ليست snapshot مضمونًا أثناء الكتابات المتزامنة.

قائمة القيود تدعم أيضًا `source=Manual|Recurring|Reversal`. المصدر محفوظ ولا يُقبل كحقل في طلب إنشاء القيد اليدوي. أضيفت مسارات `/recurring-journals` لإنشاء القوالب وتعديلها وتعطيلها وتوليد مسودة وسجل استخدام وتقويم وتدقيق، وهي موثقة بالكامل في [عقد GL04](recurring-journals.md).

ميزان المراجعة يدعم `costCenterId` و`accountLevel` و`showZero`. إغفال المستوى يعرض الحسابات القابلة للحركة؛ المستوى 1–20 يحدد أقصى عمق للعرض، ويستبدل كل مجموعة أبناء بحسابها الأب عند ذلك العمق دون جمع الأب والأبناء معًا. الحساب الفرعي الأقل عمقًا يبقى ظاهرًا. الصف يحتوي `ParentId` و`Level` و`IsPosting`. `showZero=false` يخفي الصف الخالي من الأرصدة والحركة؛ يبقى الحساب الذي له حركة متقابلة ورصيد ختامي صفر.

الأستاذ يدعم `costCenterId` و`externalReference`. المرجع مطابق تمامًا مع مراعاة حالة الأحرف، بعد إزالة الفراغات الطرفية؛ ليس بحثًا جزئيًا. يطبق المركز والمرجع على أول المدة والحركة معًا، وتعود الفروع الفعلية المصرح بها في الاستجابة. مركز تابع لفرع غير مسموح يُرفض، ومركز يتعارض مع الفرع المختار يُرفض. تصفية مركز قد تعرض جزءًا من قيد موزع على مراكز مختلفة؛ لا يُفترض اتزان كل مركز منفردًا إذا لم تتوازن أبعاد جميع أطرافه.

كل نجاح كتابة يعيد 200 و`CommandReceipt`:

```json
{"resourceId":"11111111-1111-1111-1111-111111111111","version":"22222222-2222-2222-2222-222222222222","status":"Draft","number":"JV-2026-000001"}
```

## بدء شركة تجريبية

بهوية مسؤول المنصة، أرسل إنشاء الشركة:

```json
{"id":"11111111-1111-1111-1111-111111111111","code":"DEMO","name":"شركة تجريبية","countryCode":"YE","functionalCurrency":"YER","currencyScale":2,"profile":{"legalName":"شركة تجريبية","jurisdiction":"منطقة الاختصاص التجريبية","fiscalYearStartsOn":"2026-01-01","city":"صنعاء","street":"شارع تجريبي"}}
```

أنشئ الفرع والسنة، ثم امنح مستخدم إدخال ومستخدم اعتماد مستقلين عضويتين عبر `/memberships`. تستعمل قيمة `sub` الحقيقية لكل مستخدم من جهة الهوية، وليست البريد أو اسم الدخول إلا إذا كان ذلك هو sub الفعلي.

```json
{"subject":"actual-clerk-sub","permissions":["ledger.read","journal.write","journal.post","journal.reverse"],"allBranches":true,"branchIds":[],"expectedVersion":null}
```

```json
{"subject":"actual-approver-sub","permissions":["ledger.read","journal.approve"],"allBranches":true,"branchIds":[],"expectedVersion":null}
```

إنشاء حساب نقدية فرعي ثم حساب مقابل مناسب يتم بواسطة مسؤول الإعداد. مثال بنية حساب، مع استخدام رمز فريد لكل حساب:

```json
{"code":"1101","name":"النقدية","type":"Asset","parentId":null,"isPosting":true,"isControl":false,"requiresCostCenter":false,"currencyCode":"YER","normalBalance":"Debit"}
```

تعريف الفرع `{"code":"MAIN","name":"الرئيسي","city":"صنعاء","address":"عنوان الفرع الفعلي"}` والسنة `{"code":"2026","startsOn":"2026-01-01","endsOn":"2026-12-31"}` يولد الفترات الشهرية آليًا.

مثال قيد؛ استبدل معرفات الفرع والحسابين بالقيم المنشأة فعلًا:

```json
{
  "branchId": "33333333-3333-3333-3333-333333333333",
  "documentDate": "2026-10-20",
  "accountingDate": "2026-10-20",
  "description": "قيد تجريبي متزن",
  "externalReference": null,
  "lines": [
    {"accountId":"44444444-4444-4444-4444-444444444444","costCenterId":null,"description":"الطرف المدين","currencyCode":"YER","exchangeRateId":null,"debit":"100.00","credit":"0"},
    {"accountId":"55555555-5555-5555-5555-555555555555","costCenterId":null,"description":"الطرف الدائن","currencyCode":"YER","exchangeRateId":null,"debit":"0","credit":"100.00"}
  ]
}
```

العملة الوظيفية لا تحتاج ExchangeRateId وسعرها واحد. عملة أخرى تتطلب تعريف العملة وسعرها أولًا ثم إرسال معرف أحدث سعر نافذ عند تاريخ الأثر. تحفظ نسخة السعر داخل السطر؛ إضافات الأسعار اللاحقة لا تعيد تقييم القيد السابق. لا يوجد Endpoint لإعادة تقييم الأرصدة بعد.

بعد إنشاء القيد أرسل `submit`، ثم `approve` بتوكن المستخدم المستقل، ثم `post` بصلاحية الترحيل. كل انتقال يستخدم إصدار إيصال الخطوة السابقة:

```json
{"expectedVersion":"22222222-2222-2222-2222-222222222222","reason":null}
```

`reverse` ينشئ مسودة جديدة، ولا يلغي الأثر فورًا. تحتاج المسودة إلى تقديم واعتماد وترحيل، ويُحجز أصل القيد لمنع عكس متزامن آخر. إلغاء مسودة العكس يفك الحجز. لا يُسمح بعكس القيد العكسي نفسه في هذه المرحلة.

## الأخطاء

أخطاء الأعمال بصيغة Problem Details تحتوي `code` و`traceId` ورسالة عربية. 400 لطلب غير قابل للتحليل، 401 من المصادقة عند غياب توكن صالح، 403 للصلاحيات، 404 لمرجع غير موجود في النطاق، 409 لتعارض إصدار/حالة/مفتاح/قيد قاعدة بيانات، 422 لفشل تحقق الأعمال، و503 لتعذر SQL. الأخطاء غير المتوقعة 500 دون كشف التفاصيل الداخلية للعميل. استجابات 401 الصادرة من middleware قد تكون بلا جسم JSON.

رموز شائعة: `journal_unbalanced`، `period_closed`، `self_approval_forbidden`، `branch_access_denied`، `version_conflict`، `idempotency_conflict`، `already_reversed`، `control_account_protected`، `write_conflict`.

المراجع النهائية لأسماء وحقول الأوامر [مجلد العقود حسب الوظيفة](../../backend/src/Ameen.Erp.Application/Contracts) ولمساراتها [ملفات Endpoints حسب الوحدة](../../backend/src/Ameen.Erp.Api/Endpoints). الطلبات في .NET 10 ترفض الخصائص غير المعروفة والمعاملات الإلزامية المحذوفة؛ الحقول القابلة للقيمة null تُرسل صراحة عندما لا تكون اختيارية في عقد constructor.

تفاصيل GL01 وحقول `normalBalance` و`englishName` و`notes` وقواعد تعديل البنية وسجل الاستخدام في [عقد الحسابات](accounts.md).

حقول العملات والمراكز وقواعدها وحدودها في [عقد SYS04 وCOST01](currencies-and-cost-centers.md).

عقد SYS01 الإلزامي الجديد، وضوابط تعطيل الشركة وترقية الشركات القديمة في [بطاقة الشركة](companies.md).

تفاصيل البحث والتعديل والحالة والاستخدام والتدقيق للفروع في [عقد SYS02](branches.md).

إنشاء السنة يقبل `periods` اختيارية لفترات صريحة؛ الضوابط وحدود حالة الإقفال في [التقويم المالي SYS03](fiscal-calendar.md).

تفاصيل استعلام السعر النافذ وموانع إقفال الأستاذ وحدودهما في [SYS05 وGL11](rates-and-close-preview.md).

دليل الوحدات العام وحدود الدقة والتحويل في [INV03](units.md).

شجرة المجموعات وروابط الحسابات وحدود الضريبة والاستخدام في [INV02](item-groups.md).

بطاقة الصنف وقيم القوائم الأولية والمعامل والباركود والسعر والتحويل في [INV04](items.md).

امتداد INV02: `GET /api/v1/companies/{companyId}/item-groups/{id}/usage` لملخص مراجع المجموعة وموانعها، و`GET /api/v1/companies/{companyId}/item-groups/{id}/items` لصفحات الأصناف المباشرة أو الشجرة. كلاهما يتطلب `ledger.read` على جميع الفروع؛ تفاصيل المعاملات والحدود في [عقد المجموعة](item-groups.md).

واجهات INV01 تحت `/api/v1/companies/{companyId}/warehouses` لإنشاء وتعديل وقراءة وبحث وتعطيل المخازن ومواقعها وتدقيقها، ومراجع الحساب عبر `/accounts/{id}/warehouses`. [العقد والصلاحيات وحدود التنفيذ](warehouses.md).

مرشحات SYS14 على `GET /api/v1/companies/{companyId}/audit-events`: from/to بتواريخ UTC، actorId، action، resourceId، offset/limit؛ وتفاصيل الحدث عبر `/{id}`. كلاهما يتطلب audit.read على جميع الفروع. [العقد وحدود البحث](audit-log.md).

## تحديث INV05 — 2026-10-22

أضيفت InventoryIdentityService وعقود مستقلة وInventoryIdentitiesEndpoints وتهيئة EF مستقلة وترقية 012. [عقد الدفعات والتسلسل وحدوده](inventory-identities.md). الشاشة جزئية؛ لا توجد حركة مخزنية أو تكلفة. أُنجز فصل رمز البطاقة عن رقم الدفعة/التسلسل في ترقية 013؛ تبقى سياسة الجودة والصلاحية وحماية الهوية عند الاستخدام وربطها بالمستندات واختبارات الأثر المخزني.
