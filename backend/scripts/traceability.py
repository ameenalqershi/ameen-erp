#!/usr/bin/env python3
"""Generate/check explicit implementation coverage without changing the approved analysis."""
import argparse
import csv
import hashlib
import io
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ANALYSIS = ROOT / "muhaseb-erp-analysis"
OUT = ROOT / "docs/backend"

# Only explicitly reviewed fields are mapped. Unlisted fields remain pending.
# 'audit' means recoverable from the audit stream, not a direct form/response field.
MAP = {}


def screen(code, api, implemented, remaining, fields, audit=()):
    MAP[code] = dict(api=api, implemented=implemented, remaining=remaining, fields={
        f"{code}-F{n:02}": dict(status="implemented", binding=b) for n, b in fields.items()
    })
    for n, b in audit:
        MAP[code]["fields"][f"{code}-F{n:02}"] = dict(status="audit", binding=b)


screen("SYS01", "GET/POST /companies; GET/PUT /companies/{companyId}; POST /status; GET /usage|profile-audit-events",
       "بطاقة قانونية وعنوانية وإصدار عددي؛ تحرير وحالة واستخدام وتدقيق؛ قفل العملة بعد الأثر وربط بداية أول سنة؛ منع كتابة الشركة المعطلة أو ناقصة البيانات",
       "ملفات الدولة وشروط إلزام التسجيل الضريبي والتجاري والعنوان الوطني حسب الاختصاص؛ مرجع دول معتمد بدل تحقق شكل الرمز؛ مشروع تحويل العملة الوظيفية التاريخي",
       {1: "Company.Id / CreateCompanyRequest.Id", 2: "route.companyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "LegalName", 9: "CountryCode", 10: "Jurisdiction", 11: "FunctionalCurrency", 12: "CommercialRegistration", 13: "TaxRegistration", 14: "FiscalYearStartsOn", 17: "Revision", 19: "City", 20: "District", 21: "Street", 22: "BuildingNumber", 23: "PostalCode", 24: "AdditionalNumber"},
       [(15, "AuditEvent.ActorId: company.created"), (16, "AuditEvent.OccurredAt: company.created"), (18, "AuditEvent.OccurredAt: company.edited / company.status-changed")])
MAP["SYS01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["CompanyTests.cs", "CompanySqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("SYS02", "GET/POST /companies/{companyId}/branches; GET /search; GET/PUT /{id}; POST /{id}/status; GET /{id}/usage|audit-events",
       "بطاقة فرع وعنوان وشجرة بلا دورات؛ بحث ضمن الصلاحية دون توريث الفروع؛ قفل بنية المستخدم؛ مركز افتراضي متسق؛ تعديل وحالة واستخدام وتدقيق",
       "نوع الموقع وقيمه ومرجع المدير بانتظار حسم التحليل؛ المركز الافتراضي إعداد لا يملأ سطور القيود تلقائيًا؛ الشركات القديمة تستكمل عناوين فروعها قبل التشغيل",
       {1: "Branch.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ParentId", 10: "City", 11: "Address", 12: "DefaultCostCenterId", 16: "Revision"},
       [(14, "AuditEvent.ActorId: branch.created"), (15, "AuditEvent.OccurredAt: branch.created"), (17, "AuditEvent.OccurredAt: branch.edited / branch.status-changed")])
MAP["SYS02"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["BranchTests.cs", "BranchSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("SYS03", "GET/POST /companies/{companyId}/fiscal-years; GET /search|/{id}|/{id}/usage|/{id}/audit-events; GET /companies/{companyId}/periods",
       "تقويم شهري افتراضي أو فترات صريحة تغطي السنة بلا فجوات أو تداخل؛ حالة إقفال الأستاذ مشتقة؛ بحث واستخدام وتدقيق؛ حماية التزامن والترحيل",
       "تعديل تقويم قائم وإصدار عددي وإقفال السنة الشامل غير منفذ؛ لا يوجد تقويم مختلف لكل فرع؛ الفترة ذات أكثر من موعد دوري ترفض التوليد؛ لا حذف أو تعطيل بديل للإقفال",
       {1: "CompanyId", 3: "FiscalCalendar.State (GeneralLedgerOnly)", 4: "FiscalYear.Code", 5: "FiscalYear.StartsOn", 6: "FiscalYear.EndsOn", 7: "FiscalPeriod.Code / FiscalPeriodRequest.Code", 8: "FiscalPeriod.StartsOn / FiscalPeriodRequest.StartsOn", 9: "FiscalPeriod.EndsOn / FiscalPeriodRequest.EndsOn", 10: "FiscalPeriod.IsClosed"},
       [(11, "AuditEvent.ActorId: fiscal-year.created"), (12, "AuditEvent.OccurredAt: fiscal-year.created"), (14, "AuditEvent.OccurredAt: period.closed / period.reopened")])
MAP["SYS03"]["fields"]["SYS03-F13"] = dict(status="partial",
    binding="FiscalYear.Revision int? = 1 on new years, nullable for historical un-attributed rows; edit calendar + increments pending; migration 046")
MAP["SYS03"]["fields"]["SYS03-F14"] = dict(status="partial",
    binding="FiscalYear.LastModifiedAt at creation; period.closed/reopened in AuditEvent; calendar edit timestamps pending")
MAP["SYS03"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["FiscalCalendarTests.cs", "FiscalCalendarSqlIntegration.cs", "FiscalYearAuditSqlIntegration.cs", "HostedApiIntegration.cs"])
screen("SYS04", "GET/POST /companies/{companyId}/currencies; GET /search; GET/PUT /{id}; POST /{id}/status; GET /{id}/usage|audit-events",
       "بطاقة عملة بأسماء ورمز عرض واسم جزء وإصدار عددي؛ تحرير وحالة وبحث وتدقيق؛ قفل الدقة بعد الاستخدام وفحص التعطيل عند الترحيل",
       "الرمز الداخلي موحد مع رمز العملة ولا يربط بقائمة ISO خارجية وغير قابل لإعادة التسمية؛ أسماء العملة الوظيفية والبطاقات القديمة تبدأ برمز ISO وتحتاج تحريرًا؛ تحويل الدقة التاريخية غير متاح",
       {1: "Currency.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "Code", 9: "Scale", 10: "DisplaySymbol", 11: "FractionName", 14: "Revision"},
       [(12, "AuditEvent.ActorId: currency.created"), (13, "AuditEvent.OccurredAt: currency.created"), (15, "AuditEvent.OccurredAt: currency.edited / currency.status-changed")])
MAP["SYS04"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["CurrencyTests.cs", "FoundationSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("SYS05", "GET/POST /companies/{companyId}/exchange-rates; GET /search|/quote|/{id}|/{id}/usage|/{id}/audit-events",
       "سعر موجب مؤرخ غير قابل للتعديل؛ بحث بالتاريخ والعملة والمصدر وصفحات؛ سعر نافذ باتجاه صريح؛ استخدام وتدقيق؛ اختيار مشترك مع التوليد الدوري",
       "نوع السعر وقيمه ونطاق الفرع وحالة تعطيل السعر وإصداره العددي غير منفذة؛ لا تحويل تاريخي ولا إعادة تقييم أرصدة؛ المعاينة ليست حجزًا للسعر",
       {1: "CompanyId", 4: "CurrencyCode", 5: "ExchangeRateDetail.FunctionalCurrency / ExchangeRateQuote.FunctionalCurrency", 6: "EffectiveDate", 8: "Source", 9: "RateToFunctional"},
       [(10, "AuditEvent.ActorId: exchange-rate.created"), (11, "AuditEvent.OccurredAt: exchange-rate.created")])
MAP["SYS05"]["test_file"] = "backend/tests/Ameen.Erp.Tests/RateAndCloseTests.cs; backend/tests/Ameen.Erp.Tests/HostedApiIntegration.cs"
screen("SYS06", "POST /companies/{companyId}/memberships/{id}/status",
       "تعطيل وتفعيل عضوية ERP؛ ربط sub بجهة OIDC", "شاشة المستخدم الكاملة؛ إنشاء الهوية والبريد واللغة وMFA لدى مزود الهوية لم يُدمج تشغيليًا",
       {1: "Membership.Id", 2: "CompanyId"})
# SYS06 membership metadata is now recorded for fresh ERP access grants only.
# IdP-owned user profile fields (and unknown historic creators) remain outstanding.
for n, binding in {
    14: "Membership.CreatedBy: server actor for new ERP grants only; legacy/IdP identity profile unknown",
    15: "Membership.CreatedAt: server UTC time for new ERP grants only; legacy/IdP identity profile unknown",
    16: "Membership.Revision int?: creation 1, authorized grant/status increments; legacy unknown; GUID Version separate",
    17: "Membership.LastModifiedAt: server UTC time for new ERP grants/changes; legacy/IdP profile unknown",
}.items():
    MAP["SYS06"]["fields"][f"SYS06-F{n:02}"] = dict(status="partial", binding=binding)
MAP["SYS06"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["Program.cs", "MembershipAuditSqlIntegration.cs", "MembershipStatusSqlIntegration.cs", "HostedApiIntegration.cs"])
screen("SYS07", "GET/POST /companies/{companyId}/memberships; internal EF/SQL 029 role foundation only",
       "منح عمليات خادمية صريحة لمستخدم داخل شركة؛ منع تغيير المستخدم لصلاحياته؛ أساس كتالوج أدوار SYS07 وقواعد السماح/المنع وحد المبلغ/العملة وFK إلى SYS09؛ ربط مستخدم-دور SQL 030 ومقيّم مستقل غير مفعل؛ استبدال قواعد الدور ذريًا مع فحص إصدار التعديل؛ رفض لقطات الأدوار الناقصة أو العابرة للشركات وتكرار الروابط قبل تقييم المنح؛ رفض عناصر قواعد الدور الفارغة دون تغيير الدور (مجال فقط)؛ رفض اللقطات الداخلية ذات قواعد مكررة أو عابرة للشركات في تقييم الدور المباشر والمسنَد (مجال فقط)؛ رفض القواعد المحمّلة برموز غير قياسية أو سقف/عملة غير متسق وصفوف اللقطات الفارغة (مجال فقط)",
       "إدارة الأدوار وربطها بالعضويات عبر API وإنفاذ المنع الصريح ونطاق الفرع والمبلغ والعملة والعمليات المنفصلة والتدقيق وقبول API؛ كتابة الأدوار والروابط محجوبة",
       {1: "CompanyId", 7: "GrantedPermissions[]", 13: "Version"}, [(11, "AuditEvent.ActorId"), (12, "AuditEvent.OccurredAt")])
for n, binding in {
    2:"SecurityRole.BranchId: tenant FK only", 3:"SecurityRole.IsActive: stored only",
    4:"SecurityRole.Name: internal model only", 5:"SecurityRole.Responsibility: internal model only",
    6:"SecurityRoleRule.ScreenCode: internal model only", 7:"SecurityRoleRule.OperationCode: internal model only", 8:"SecurityRoleRule.Allowed: no runtime enforcement",
    9:"SecurityRoleRule.AmountLimit: decimal(20,6), no runtime enforcement",
    10:"SecurityRoleRule.CurrencyId: tenant FK, no runtime enforcement",
    14:"SecurityRole.LastModifiedAt: internal server timestamp only"
}.items():
    MAP["SYS07"]["fields"][f"SYS07-F{n:02}"] = dict(status="partial", binding=binding)
MAP["SYS07"]["test_file"] = "backend/tests/Ameen.Erp.Tests/SecurityRoleFoundationTests.cs; backend/tests/Ameen.Erp.Tests/SecurityRoleSqlIntegration.cs; backend/tests/Ameen.Erp.Tests/SecurityRoleAssignmentSqlIntegration.cs"
screen("SYS08", "GET/POST /companies/{companyId}/memberships",
       "نطاق الشركة والفروع مطبق على الأوامر والتقارير؛ ربط عضوية-دور محمي SQL 030 دون إنفاذ", "نطاق الحساب والمخزن والصندوق والعملاء ومراكز التكلفة المستقل؛ تفعيل إدارة الدور وربطه بالمستخدم",
       {1: "CompanyId", 4: "Subject", 6: "AllBranches / BranchIds[]", 13: "Version"}, [(11, "AuditEvent.ActorId"), (12, "AuditEvent.OccurredAt")])
MAP["SYS08"]["fields"]["SYS08-F05"] = dict(status="partial", binding="SecurityRoleAssignment.RoleId: tenant FK and guarded write only; no API")
MAP["SYS08"]["test_file"] = "backend/tests/Ameen.Erp.Tests/SecurityRoleFoundationTests.cs; backend/tests/Ameen.Erp.Tests/SecurityRoleAssignmentSqlIntegration.cs"
screen("SYS09", "internal EF/SQL 028 only; no public SYS09 API; existing journal separation policy remains independent",
       "SYS09 dependency foundation: tenant-scoped route and stage schema, amount decimal(20,6), optional branch, currency FK, audit revision, stage uniqueness and GL14 route FK; domain-only conservative branch/amount overlap preflight with hydrated stage/audit integrity and cross-route stage identity uniqueness, and atomic stage replacement with revision; all unvalidated writes blocked",
       "SYS07 role FK exists but membership/active/scope/deny enforcement is absent; document type allowed list, transactional overlap/scope arbitration, cross-currency policy, stage execution, notifications, authorization, endpoints and acceptance not implemented",
       {1: "CompanyId: existing company boundary"})
for n, binding in {
    2:"ApprovalRoute.BranchId: optional tenant FK only", 3:"IsActive: stored state only",
    4:"DocumentType: text placeholder; allowed list not defined", 5:"MinimumAmount: decimal(20,6) only",
    6:"MaximumAmount: nullable decimal(20,6) only", 7:"CurrencyId: tenant FK only",
    8:"SeparateCreatorAndApprover: stored flag, not wired to approval engine",
    9:"ApprovalRouteStage.StageNumber: positive, unique per route; cross-route stage PK integrity preflight and domain-only atomic replacement",
    10:"ApproverRoleId: required tenant SYS07 role FK via SQL 029; role policy not validated; persistence blocked",
    11:"AlertAfterMinutes: nullable nonnegative; no automatic approval",
    12:"CreatedBy: actor field; hydrated audit preflight only, no API", 13:"CreatedAt: datetimeoffset; hydrated audit preflight only",
    14:"Revision: server integer, domain-only expected-revision editing and snapshot check", 15:"LastModifiedAt: datetimeoffset; hydrated audit chronology preflight"
}.items():
    MAP["SYS09"]["fields"][f"SYS09-F{n:02}"] = dict(status="partial", binding=binding)
MAP["SYS09"]["test_file"] = "backend/tests/Ameen.Erp.Tests/ApprovalRouteFoundationTests.cs; backend/tests/Ameen.Erp.Tests/ApprovalRouteSqlIntegration.cs"
screen("SYS10", "GET/POST /companies/{companyId}/number-series; GET/PUT /{id}; POST /status|reserve; GET /usage|audit-events",
       "سلاسل عامة أو لفرع وسنة ونوع مستند وبادئة ورقم تال قابل للإعداد؛ حجز ذري موثق لا يعاد بعد الإلغاء؛ ربط اليومية والافتتاحات؛ تعديل وتعطيل وبحث واستخدام وإصدار وتدقيق مع حماية السجلات التاريخية",
       "أنواع التشغيل الحالية Journal وInventoryOpening؛ الترقيم الضريبي مرفوض حتى TAX05 وملف الدولة؛ باقي أنواع المستندات تربط عند تنفيذ مصادرها ولا تدعى جاهزيتها",
       {1:"CompanyId",2:"BranchId: optional company-wide or explicit branch, global configuration/read requires all branches",3:"IsActive",4:"DocumentKind: Journal/InventoryOpening; TaxInvoice guarded by country prerequisite",5:"FiscalYearId",6:"Prefix: optional user text",7:"NextNumber: monotonic server allocation and explicit forward edit",8:"CreatedBy",9:"CreatedAt",10:"Revision: integer separate from Version UUID",11:"LastModifiedAt"})
MAP["SYS10"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/"+name for name in ["NumberingTests.cs","NumberingSqlIntegration.cs","SchemaUpgradeIntegration.cs","HostedApiIntegration.cs"])

screen("GL14", "internal EF model and SQL 027 only; no public GL14 API",
       "GL14-01 structural foundation: JournalDocumentType with tenant-scoped branch/number-series references, unique text code, origin, attachment flag and server audit; domain-only origin compatibility and optimistic revision checks; SQL 027 atomic and idempotent; persistence rejects unvalidated SYS09 approval routes",
       "SYS09 route schema and GL14 tenant FK exist but roles, route activation and policy validation are blocked; create/edit/disable/search/usage/audit APIs, GL03 binding, runtime enforcement of manual/automatic origin and reversal separation, and full acceptance remain unimplemented",
       {})
for n, binding in {
    1:"CompanyId: EF/SQL tenant key only", 2:"BranchId: nullable tenant FK only",
    3:"IsActive: persisted server state only", 4:"Code: nvarchar(32) unique per company only",
    5:"Name: nvarchar(200) only", 6:"Origin: Manual/Automatic enum plus domain-only MatchesSource guard; unknown hydrated values fail closed in domain acceptance; no GL03 enforcement",
    7:"NumberSeriesId: tenant FK; active/scope checks pending", 8:"ApprovalRouteId: required tenant FK via SQL 028; active route validation pending",
    9:"RequiresAttachment: bit only", 10:"CreatedBy: server actor field, no service",
    11:"CreatedAt: datetimeoffset only", 12:"Revision: server int plus expected-revision domain guard; no API concurrency acceptance",
    13:"LastModifiedAt: datetimeoffset only"
}.items():
    MAP["GL14"]["fields"][f"GL14-F{n:02}"] = dict(status="partial", binding=binding)
MAP["GL14"]["test_file"] = "backend/tests/Ameen.Erp.Tests/JournalDocumentTypeFoundationTests.cs; backend/tests/Ameen.Erp.Tests/JournalDocumentTypeSqlIntegration.cs"

screen("GL13", "GET/POST /companies/{companyId}/financial-mappings; GET/PUT /{id}; POST /status|review; GET /usage|audit-events; GET /reviews/{id}",
       "حقول رأس وبنود الربط كاملة؛ صيغ آمنة + و- ومراجع بنود مع منع الدورات والتكرار وتداخل المجموعات؛ مراجعة غير المصنف؛ لقطات أرصدة وظيفية مرحّلة ثابتة وبحث وتعطيل واستخدام وتدقيق ذري",
       "GL13 خريطة ومراجعة لا تنفيذ تقارير RPT03/RPT04 كاملة؛ النطاق الاختياري للفرع مرشح للأستاذ ويتطلب تفويض جميع الفروع لدليل الحسابات؛ ParentCode للعرض فقط والتجميع بمعادلة صريحة؛ لا لغة ضرب/قسمة أو قواعد تدفق نقدي مفترضة",
       {1:"CompanyId from route",2:"BranchId: optional company-wide or explicit active branch",3:"IsActive: server state",4:"Name",5:"StatementKind: FinancialPosition/ProfitLoss/ChangesInEquity/CashFlow",6:"VersionCode: explicit text code",7:"EffectiveFrom",8:"Lines.Code",9:"Lines.ParentCode: optional",10:"Lines.Title",11:"Lines.AccountId: posting account or group with posting descendants",12:"Lines.DisplaySign: Normal/Reverse required",13:"Lines.Formula: optional explicit safe aggregation; no account on formula row",14:"CreatedBy: server actor",15:"CreatedAt: server clock",16:"Revision: server integer distinct from Version UUID",17:"LastModifiedAt: server clock"})
MAP["GL13"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/"+name for name in ["FinancialMappingTests.cs","FinancialMappingSqlIntegration.cs","SchemaUpgradeIntegration.cs","HostedApiIntegration.cs"])

screen("SYS14", "GET /companies/{companyId}/audit-events?from=&to=&actorId=&action=&resourceId=; GET /audit-events/{id}",
       "قراءة سجل أحداث غير قابل للتعديل وتفاصيل الحدث؛ مرشحات UTC والمستخدم التاريخي ونوع الحدث والمرجع وصفحات؛ مقارنة هوية وحدث حساسة للحالة",
       "مرشح الشاشة ورقم المستند ونطاق الفروع المستقل والتصدير وتدقيق القراءة غير منفذة؛ التاريخ اختياري للتوافق؛ المستخدم هو OIDC sub التاريخي ونوع الحدث مفتاح محفوظ دون كتالوج شاشة معتمد",
       {1: "CompanyId", 3: "from: inclusive UTC date", 4: "to: inclusive UTC date", 9: "action: stored action key", 10: "actorId: historical OIDC sub"})
for n in (3, 4, 9, 10):
    MAP["SYS14"]["fields"][f"SYS14-F{n:02}"]["status"] = "partial"
MAP["SYS14"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["AuditTests.cs", "AuditSqlIntegration.cs", "HostedApiIntegration.cs"])
screen("GL01", "GET/POST /companies/{companyId}/accounts; GET/PUT /accounts/{id}; POST /status; GET /usage|audit-events",
       "بطاقة ثنائية اللغة وملاحظات وطبيعة مدينة/دائنة صريحة؛ تعديل وشجرة بلا دورات وقفل بنية المستخدم وسجل استخدام وتغييرات",
       "مسار تحويل معتمد لتغيير بنية الحساب المستخدم؛ سجل الاستخدام الحالي للأستاذ والقوالب فقط حتى تنفيذ الدفاتر المساعدة",
       {1: "Account.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ParentId", 9: "Type", 10: "IsPosting", 11: "NormalBalance: Debit / Credit (required on create/edit)", 12: "CurrencyCode", 13: "IsControl", 14: "RequiresCostCenter", 17: "Revision (server integer); Version is a separate UUID concurrency token"},
       [(15, "AuditEvent.ActorId: account.created"), (16, "AuditEvent.OccurredAt: account.created"), (18, "AuditEvent.OccurredAt: account.edited / account.status-changed; /audit-events")])
MAP["GL01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["AccountTests.cs", "AccountSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("GL02", "GET/POST /companies/{companyId}/general-openings; GET/PUT /{id}; GET /{id}/settlement-preview|impact-preview|print (printable RTL HTML); journal submit|approve|return|post|cancel|reverse and processing APIs",
       "افتتاح عام مصدره GeneralOpening ومرجع ميزان وتاريخ تشغيل وعملة وظيفية أو أجنبية بسعر تاريخي مثبت؛ قيد متزن ودورة مستقلة واعتماد وترحيل وعكس تاريخي؛ منع الحسابات الرقابية؛ معاينة حسابات التسوية التاريخية للمخزون دون عد القيد مرتين؛ إصدار ووقت تعديل يشملان حجز العكس وإلغاءه وترحيله؛ استبدال رأس المسودة والسطور مع نقل الفرع والفترة وترقيم السنة وإعادة التحقق؛ معاينة الأثر التفصيلية للمدين والدائن وسعر السطر والفرق دون تسجيل حركة أو تغيير القيد",
       "الطباعة متاحة كصفحة RTL HTML للمتصفح فقط؛ تصدير PDF/اختيار قالب رسمي غير معتمد؛ استيراد الافتتاح وتصفير حساب التسوية بوابة شاملة غير منفذ؛ الذمم والمصادر غير المنفذة ليست مطابقة",
       {1: "JournalEntry.Id", 2: "CompanyId", 3: "BranchId", 5: "Number (JV yearly sequence)", 6: "DocumentDate", 7: "AccountingDate", 10: "Description", 11: "ExternalReference", 12: "State with linked historical reversal", 13: "OperationStartsOn", 14: "PreviousTrialBalanceReference", 15: "Lines.AccountId: non-control only", 16: "Lines.Debit", 17: "Lines.Credit", 18: "Lines.CostCenterId", 19: "CreatedBy", 20: "CreatedAt"})
MAP["GL02"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["GeneralOpeningTests.cs", "GeneralOpeningReversalTests.cs", "GeneralOpeningPrintSqlIntegration.cs", "DraftJournalHeaderTests.cs", "DraftHeaderSqlIntegration.cs", "JournalProcessingSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
for n, binding, status in [(4, "FiscalPeriod.FiscalYearId via settlement-preview", "derived"), (8, "OpeningCurrencyCode: active functional or foreign opening currency", "implemented"), (9, "OpeningExchangeRate: dated FX snapshot equal to latest effective rate; exact reversal", "implemented"), (21, "OpeningRevision: content, workflow and reversal reservation/release/post", "implemented"), (22, "OpeningModifiedAt: content, workflow and reversal reservation/release/post", "implemented")]:
    MAP["GL02"]["fields"][f"GL02-F{n:02}"] = dict(status=status, binding=binding)
screen("GL03", "GET/POST /companies/{companyId}/journals; GET/PUT /journals/{id}; GET /journals/{id}/impact-preview|print (manual HTML); POST /companies/{companyId}/journals/reversals/run-due",
       "قيد يدوي متعدد العملات والمراكز بدورة مستقلة وأرقام وسعر تاريخي؛ تعديل رأس المسودة والسطور مع إعادة ربط الفترة وترقيم السنة والتحقق من السعر النافذ؛ حقلا العكس التلقائي المشروط وموعده؛ توليد مسودة عكس مرتبطة عند الاستحقاق وفق صلاحيات Reverse/Draft ونطاق الفرع مع منع التكرار حتى بعد إلغاء المسودة؛ مشغل مفوض اختياري لا يعتمد أو يرحّل القيد تلقائيًا؛ معاينة أثر القيد اليدوي والعكس قبل التقديم بسطور المبالغ الأصلية والمحوّلة وفرق الاتزان وفحص الموانع دون تعديل الأستاذ أو التدقيق",
       "المشروع والطرف والمرفقات وأنواع القيود GL14؛ تفعيل عامل العكس المجدول في بيئة النشر مشروط بإعداد ActorSubject المفوض؛ لا يعد توليد مسودة العكس موافقة أو ترحيلًا",
       {1: "JournalEntry.Id", 2: "CompanyId", 3: "BranchId", 5: "Number", 6: "DocumentDate", 7: "AccountingDate", 10: "Description", 11: "ExternalReference", 12: "State", 14: "AutomaticReversal boolean in create/edit/detail/list; default false for legacy records", 15: "ReversalDate conditional input / AutomaticReversalDate nullable historical due date; SQL 026", 16: "Lines[].AccountId", 17: "Lines[].Debit", 18: "Lines[].Credit", 19: "Lines[].CurrencyCode", 20: "Lines[].ExchangeRate (ExchangeRateId on input)", 21: "Lines[].CostCenterId", 24: "Lines[].Description", 25: "CreatedBy", 26: "CreatedAt", 27: "Revision: server integer; Version UUID is a separate concurrency token"},
       [(28, "AuditEvent.OccurredAt: journal.edited")])
MAP["GL03"]["fields"]["GL03-F28"] = dict(status="implemented", binding="LastModifiedAt server datetimeoffset; successful edit/workflow/reversal link mutation; atomic with audit and receipt")
MAP["GL03"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["JournalAuditTests.cs", "BalancedJournalLedgerSqlAcceptance.cs", "JournalPrintTests.cs", "JournalPrintSqlIntegration.cs", "DraftJournalHeaderTests.cs", "DraftHeaderSqlIntegration.cs", "JournalImpactPreviewTests.cs", "ScheduledReversalTests.cs", "ScheduledReversalSqlIntegration.cs", "ScheduledReversalHostedIntegration.cs", "HttpIntegration.cs", "SqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs", "OpenApiSnapshotTests.cs"])
screen("GL04", "GET/POST /companies/{companyId}/recurring-journals; GET/PUT /recurring-journals/{id}; POST /status|generate|run-due; GET /generations|schedule|audit-events",
       "قالب شهري أو ربع سنوي أو سنوي بتاريخ نهاية اختياري دون تاريخ مصطنع؛ توليد يدوي أو مجدول لمسودة واحدة لكل فترة مع مشغّل مفوض عند تفعيله؛ رقم إصدار تعديل صحيح وآخر تعديل خادمي؛ مصدر وإصدار تاريخي ومواعيد ثابتة وأسعار نافذة؛ بحث وتعطيل وسجل استخدام وتغييرات",
       "الفرع في المخطط شرطي لكن العقد الحالي يتطلبه (GL04-F02)؛ خيارات دورية أخرى غير معتمدة؛ يلزم تفعيل المشغل على الإنتاج",
       {1: "CompanyId", 2: "BranchId", 3: "IsActive", 4: "Name", 5: "Frequency", 6: "StartsOn", 7: "EndsOn", 9: "Lines[].AccountId",
        10: "Lines[].Debit", 11: "Lines[].Credit", 12: "Lines[].CostCenterId", 13: "CreatedBy", 14: "CreatedAt", 15: "Revision: integer (Version UUID is a separate concurrency token)"},
       [(16, "AuditEvent.OccurredAt: recurring.edited / recurring.status-changed; scoped /audit-events")])
MAP["GL04"]["fields"]["GL04-F02"] = dict(status="partial", binding="BranchId required; analysis marks branch conditional, no company-wide template policy yet")
MAP["GL04"]["fields"]["GL04-F07"] = dict(status="implemented", binding="EndsOn nullable/optional in create, edit, detail, list and SQL schema 021; null has no fabricated date limit; bounded fiscal-period scan; historical generations retained")
MAP["GL04"]["fields"]["GL04-F16"] = dict(status="implemented", binding="LastModifiedAt server datetimeoffset; audit changes retained")
MAP["GL04"]["fields"]["GL04-F08"] = dict(status="implemented", binding="GenerationMode: Manual or Scheduled; due-only draft runner and opt-in authorized worker; no automatic approval/posting")
MAP["GL04"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["RecurringTests.cs", "RecurringSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("GL05", "POST /companies/{companyId}/journals/{id}/submit|approve|return; GET /journals/processing-queue; GET /{id}/processing-preview|processing-history; POST /journals/process-batch",
       "قائمة مراجعة حسب الفترة والفرع والحالة والمصدر والتسلسل؛ معاينة موانع كل قيد واعتماد أو إعادة جماعية بمعاملة لكل مستند وسجل قرار محفوظ وإعادة آمنة", "مراجع مستقل عن المعتمد ومراحل اعتماد متعددة؛ حالة مراجعة مستقلة عن حالة القيد وإصدار عددي للإجراء",
       {1: "route.companyId", 2: "JournalEntry.BranchId", 3: "JournalEntry.State", 7: "JournalEntry.Number", 13: "Version"},
       [(6, "AuditEvent.ActorId: journal.approve / journal.return"), (8, "AuditEvent.Action / Details.after"), (9, "AuditEvent.Details.reason: journal.return"), (11, "AuditEvent.ActorId"), (12, "AuditEvent.OccurredAt")])
screen("GL06", "POST /companies/{companyId}/journals/{id}/post; GET /journals/processing-queue; GET /{id}/processing-preview|processing-history; POST /journals/process-batch; GET /journals/processing-batches/by-key/{batchKey}",
       "ترحيل جماعي مستقل لكل قيد مع إيصال ومفتاح مستند وتدقيق وOutbox؛ نتائج نجاح وفشل صريحة ومعاينة وسجل رفض التحقق؛ إعادة الناجح دون تكرار وتراجع الفاشل؛ رأس دفعة دائم برقم إصدار وتوقيت خادمي وسجل إخفاق تقني خارج معاملة المستند مع استئناف آمن", "توجيه مستندات المصادر الأخرى وعامل Outbox وتشغيله في الإنتاج؛ ربط سياسة مسارات الاعتماد متعددة المراحل مع SYS09 غير مكتمل؛ ملف الدولة والمصادر الخارجية لم تُنفذ",
       {1: "route.companyId", 2: "JournalEntry.BranchId", 3: "State", 6: "PostedAt", 7: "route.id", 9: "CommandReceipt.ResourceId / Number",
        10: "JournalProcessingBatch.ActorId / JournalProcessingDurableResult.CreatedBy: server-recorded actor; owner-scoped durable read", 11: "JournalProcessingBatch.CreatedAt: server time",
        12: "JournalProcessingBatch.Revision: integer independent of Version UUID",
        13: "JournalProcessingBatch.LastModifiedAt: server time; recorded per-item audit"})
for code in ["GL05", "GL06"]:
    MAP[code]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["JournalProcessingTests.cs", "JournalProcessingSqlIntegration.cs", "HostedApiIntegration.cs", "Program.cs"])
MAP["GL05"]["fields"]["GL05-F04"] = dict(status="implemented", binding="processing-queue.periodId: required")
for n, binding in {
    10: "JournalProcessingBatch.ActorId / JournalProcessingDurableResult.CreatedBy: server-side approval actor",
    11: "JournalProcessingBatch.CreatedAt / DurableBatch.CreatedAt: server datetimeoffset",
    12: "JournalProcessingBatch.Revision / DurableBatch.Revision: server numeric revision, replay-stable",
    13: "JournalProcessingBatch.LastModifiedAt / DurableBatch.LastModifiedAt: server datetimeoffset",
}.items():
    MAP["GL05"]["fields"][f"GL05-F{n:02}"] = dict(status="implemented", binding=binding)
MAP["GL05"]["api"] += "; GET /journals/processing-batches/by-key/{batchKey} (actor-owned audit fields)"
MAP["GL05"]["test_file"] += "; backend/tests/Ameen.Erp.Tests/JournalReviewAuditFieldsSqlAcceptance.cs"

MAP["GL06"]["fields"]["GL06-F04"] = dict(status="partial", binding="processing-queue.source: journal provenance only; source documents route separately")
MAP["GL06"]["fields"]["GL06-F05"] = dict(status="implemented", binding="processing-queue.numberRange: selected fiscal-year JV number or inclusive range; sequenceFrom/sequenceTo backward-compatible; authorized branch/state/source scoped")
MAP["GL06"]["fields"]["GL06-F08"] = dict(status="implemented", binding="JournalProcessingPreview.CanExecute/ErrorCode; JournalProcessingRow.ErrorCode/Receipt/RejectionRecorded")
screen("GL07", "POST /companies/{companyId}/journals/{id}/reverse; GET /companies/{companyId}/journals/{id}/impact-preview|print (reversal HTML); POST /companies/{companyId}/journals/reversals/run-due",
       "مسودة عكس مرتبطة ثم اعتماد وترحيل؛ نسخ المبالغ التاريخية حرفيًا؛ تشغيل العكس المجدول للقيود اليدوية المرحّلة المستحقة فقط عبر فحص مفوض وفرعي مع إنشاء مسودة عكس مستقلة بلا اعتماد/ترحيل آلي؛ سجل تاريخي يمنع تكرار التوليد بعد الإلغاء؛ ترقية SQL 026 وعكس ذري قابل لإعادة المحاولة؛ معاينة أثر مسودة العكس بسعر ومبالغ تاريخية معكوسة حرفيًا وفرق اتزان قبل الاعتماد دون تعديل الأستاذ أو سجل التدقيق",
       "عكس مستندات الدفاتر المساعدة وفحص التبعيات والضريبة؛ مشغل العكس اختياري وغير مثبت التفعيل في الإنتاج؛ الأنواع غير اليدوية تظل تعكس من مستند المصدر",
       {1: "JournalEntry.Id", 2: "CompanyId", 3: "BranchId", 5: "Number", 6: "DocumentDate", 7: "AccountingDate", 10: "Description", 11: "ExternalReference", 12: "State", 13: "ReversesJournalId", 14: "ReversalReason", 15: "AccountingDate", 16: "CreatedBy", 17: "CreatedAt", 18: "JournalEntry.Revision: server integer separate from Version UUID", 19: "JournalEntry.LastModifiedAt: server datetimeoffset, shared journal audit"})
MAP["GL07"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/"+name for name in
    ["JournalAuditTests.cs", "JournalPrintTests.cs", "JournalPrintSqlIntegration.cs", "JournalImpactPreviewTests.cs", "ScheduledReversalTests.cs", "ScheduledReversalSqlIntegration.cs", "ScheduledReversalHostedIntegration.cs",
     "HttpIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs", "OpenApiSnapshotTests.cs"])
screen("GL11", "POST /companies/{companyId}/periods/{id}/close; GET /companies/{companyId}/periods/{id}/close-preview|execution-history",
       "معاينة إقفال الأستاذ بشروط مفصلة وموانع مقسمة؛ تنفيذ يعيد فحص القيود المعلقة واتزان المرحل داخل المعاملة", "إقفال ERP شامل ومطابقة بنوك وذمم ومخزون وضريبة وأصول وقيد نتائج السنة وإصدار عددي للإجراء",
       {1: "route.companyId", 4: "route.id (period only)", 7: "PeriodClosePreview.Checks.Code (GL only)", 8: "PeriodClosePreview.Checks.Passed (GL only)"}, [(9, "AuditEvent.ActorId: period.closed"), (10, "AuditEvent.ActorId"), (11, "AuditEvent.OccurredAt")])
MAP["GL11"]["test_file"] = "backend/tests/Ameen.Erp.Tests/RateAndCloseTests.cs; backend/tests/Ameen.Erp.Tests/PeriodExecutionHistorySqlIntegration.cs; backend/tests/Ameen.Erp.Tests/HostedApiIntegration.cs; backend/tests/Ameen.Erp.Tests/SqlIntegration.cs"
screen("GL12", "POST /companies/{companyId}/periods/{id}/reopen; GET /companies/{companyId}/periods/{id}/reopen-history",
       "فتح فترة بصلاحية مستقلة وسبب مسجل؛ عرض نتيجة/سجل فتح مفوض بخصوصية PeriodReopen+Audit+Read وفصل أحداث الفتح عن الإقفال، مع ترقيم الصفحات بلا أثر مالي",
       "مرجع موافقة مستقل ونافذة فتح مؤقتة وإعادة إقفال تلقائي، وفحوص إقرارات SYS14/الذمم عند إعادة الفتح، لم تنفذ؛ لا يعتبر سجل الفتح شهادة بهذه الموافقات",
       {1: "route.companyId", 4: "route.id", 5: "ChangePeriodRequest.Reason", 10: "Version"}, [(8, "AuditEvent.ActorId"), (9, "AuditEvent.OccurredAt")])
MAP["GL12"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["RateAndCloseTests.cs", "PeriodReopenHistorySqlIntegration.cs", "HostedApiIntegration.cs", "SqlIntegration.cs"])
screen("COST01", "GET/POST /companies/{companyId}/cost-centers; GET /search; GET/PUT /{id}; POST /{id}/status; GET /{id}/usage|audit-events",
       "شجرة مراكز بلا دورات وبنطاق فرع متسق؛ أسماء وملاحظات ومسؤول فعال وإصدار عددي؛ تعديل وتعطيل وبحث واستخدام وتدقيق",
       "قائمة نوع المركز غير محددة في التحليل وتنتظر توضيح المستخدم؛ الموازنات والتوزيعات وتقارير تجميع الشجرة لم تنفذ",
       {1: "CostCenter.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ParentId", 9: "BranchId", 10: "ResponsibleMembershipId -> active company Membership with center branch scope", 14: "Revision"},
       [(12, "AuditEvent.ActorId: cost-center.created"), (13, "AuditEvent.OccurredAt: cost-center.created"), (15, "AuditEvent.OccurredAt: cost-center.edited / cost-center.status-changed")])
MAP["COST01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["CostCenterTests.cs", "FoundationSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV06", "GET/POST /companies/{companyId}/inventory-openings; GET/PUT /{id}; POST /{id}/submit|return|approve|cancel|post|correct-reversal|post-reversal; GET/POST /{id}/reversals; GET /{id}/valuation-preview|audit-events; GET/PUT /posting-settings; GET /companies/{companyId}/stock-movements",
       "تجهيز ومراجعة افتتاح ثم ترحيل ذري لحركة كمية وقيمة وقيد رقابة وOutbox؛ إعداد حساب التسوية ومعاينة الأثر؛ عكس مصدر معتمد بقيم تاريخية وحركات سالبة وقيد مرتبط؛ منع الافتتاح والعكس المكرر ومطابقة الإقفال وعزل الفروع",
       "تسوية افتتاح GL02 الشاملة وإعادة افتتاح مصحح والعكس الجزئي والطباعة ومحرك تقييم الصرف؛ الأجنبية وسياسة القطع والجودة والمواقع وإدخال التسلسل النصي غير مكتملة",
       {1: "InventoryOpening.Id", 2: "CompanyId", 3: "BranchId", 4: "FiscalYearId derived from FiscalPeriod", 5: "Number: IO per company/year initial sequence", 6: "DocumentDate", 7: "AccountingDate", 10: "Description", 11: "ExternalReference", 13: "CutoffDate", 14: "WarehouseId", 15: "CountReference", 16: "Lines.ItemId", 17: "Lines.UnitId", 18: "Lines.Quantity and exact BaseQuantity snapshot", 19: "Lines.UnitCost in input unit", 20: "Lines.IdentityId for lot-tracked item", 22: "CreatedBy", 23: "CreatedAt", 24: "Revision", 25: "ModifiedAt"})
for n, binding in {8: "CurrencyCode: functional currency only", 9: "ExchangeRate: fixed to 1 in initial scope", 21: "IdentityId selects serial; SerialNumber snapshot output instead of free-text input"}.items():
    MAP["INV06"]["fields"][f"INV06-F{n:02}"] = dict(status="partial", binding=binding)
MAP["INV06"]["fields"]["INV06-F12"] = dict(status="implemented", binding="State: Draft/Submitted/Returned/Approved/Cancelled/Posted; linked reversal document with its own workflow")
MAP["INV06"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["InventoryOpeningTests.cs", "InventoryOpeningPostingTests.cs", "InventoryOpeningReversalTests.cs", "InventoryOpeningSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV05", "GET/POST /companies/{companyId}/items/{itemId}/lots|serials; GET/PUT /{id}; POST /{id}/status; GET /{id}/audit-events",
       "بطاقات هوية دفعات وتسلسل؛ بحث وتعديل وحالة وتدقيق وتزامن؛ فحص التاريخ وقفل تتبع الصنف؛ دون أثر مخزني",
       "اعتماد الجودة وإلزام الصلاحية؛ إزالة افتراض الرقم القديم عند غيابه بعقد إصدار جديد؛ الاستخدام والضمان وربط المستندات وقيود الهوية المستخدمة غير منفذة",
       {1: "InventoryIdentity.Id", 2: "CompanyId", 3: "Code (independent card code)", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ItemId", 11: "ProductionDate", 16: "Revision"},
       [(14, "AuditEvent.ActorId: inventory-lot.created / inventory-serial.created"), (15, "AuditEvent.OccurredAt: created"), (17, "AuditEvent.OccurredAt: edited / status-changed")])
for n, binding in {9: "LotNumber independent; legacy creation defaults omitted number to Code", 10: "SerialNumber independent; legacy creation defaults omitted number to Code", 12: "ExpiryDate required for ShelfLifeDays; final conditional policy pending", 13: "QualityStatus: initial inferred Available/Quarantine/Rejected"}.items():
    MAP["INV05"]["fields"][f"INV05-F{n:02}"] = dict(status="partial", binding=binding)
MAP["INV05"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["InventoryIdentityTests.cs", "InventoryIdentitySqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV01", "GET/POST /companies/{companyId}/warehouses; GET/PUT /{id}; POST /{id}/status; GET /{id}/audit-events; GET /accounts/{id}/warehouses",
       "بطاقة مخزن ومواقع وحقول ثنائية اللغة وإصدار؛ أمين من عضوية فعالة في الفرع؛ بحث مقيد بالفروع وتعديل وحالة وتدقيق؛ حماية مراجع الحساب والفرع",
       "نوع Standard/Quarantine ومرجع الأمين Membership نطاق أولي غير محسوم؛ نطاق صلاحيات مخزن مستقل والاستخدام المستندي ومتـاح البيع والحركات وقفل المواقع بعد الحركة وأولوية حساب المخزن/المجموعة غير منفذة",
       {1: "Warehouse.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "BranchId", 9: "KeeperMembershipId", 10: "Type", 11: "InventoryAccountId", 12: "TrackLocations", 13: "Locations[].Code", 14: "Locations[].Description", 17: "Revision"},
       [(15, "AuditEvent.ActorId: warehouse.created"), (16, "AuditEvent.OccurredAt: warehouse.created"), (18, "AuditEvent.OccurredAt: warehouse.edited / warehouse.status-changed")])
MAP["INV01"]["fields"]["INV01-F09"] = dict(status="partial", binding="KeeperMembershipId: initial active ERP membership with branch scope")
MAP["INV01"]["fields"]["INV01-F10"] = dict(status="partial", binding="Warehouse.Type: Standard/Quarantine initial inferred scope")
MAP["INV01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["WarehouseTests.cs", "WarehouseSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV02", "GET/POST /companies/{companyId}/item-groups; GET/PUT /{id}; POST /{id}/status; GET /{id}/audit-events|usage|items; GET /accounts/{id}/item-groups",
       "شجرة مجموعات بلا دورات وربط حسابات المبيعات والمخزون والتكلفة؛ بحث وتعديل وحالة وتدقيق؛ ملخص الاستخدام المباشر والفرعي وأصنافه بصفحات مع أسباب القفل؛ حماية بنية الحساب المرتبط ومنع تعطيله مع مجموعة فعالة",
       "مرجع الضريبة الافتراضي وسياسة تجاوز إعدادات المجموعة غير منفذين؛ استخدام الأصناف يقفل البنية وحسابا المخزون والتكلفة مطلوبان لغير الخدمة؛ لا ترحيل مخزون",
       {1: "ItemGroup.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ParentId", 9: "InventoryAccountId", 10: "SalesAccountId", 11: "CostAccountId", 15: "Revision"},
       [(13, "AuditEvent.ActorId: item-group.created"), (14, "AuditEvent.OccurredAt: item-group.created"), (16, "AuditEvent.OccurredAt: item-group.edited / item-group.status-changed")])
MAP["INV02"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["ItemGroupTests.cs", "ItemGroupUsageTests.cs", "ItemGroupSqlIntegration.cs", "ItemSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV03", "GET/POST /companies/{companyId}/units; GET/PUT /{id}; POST /{id}/status; GET /{id}/audit-events|items",
       "دليل وحدات بأسماء ورمز قياسي ووصف ودقة كمية وإصدار عددي؛ بحث وتعديل وحالة وتدقيق؛ لا معامل تحويل عام",
       "عرض أصناف الوحدة وقفل البنية بعد ربط ITEM_UNIT منفذان؛ تحويل الوحدة والباركود في INV04؛ لا حركة مخزون بعد؛ الرمز القياسي تحقق شكلي بلا سجل خارجي؛ دقة كمية 0..6 حد تقني معلن",
       {1: "UnitOfMeasure.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "StandardCode", 9: "QuantityScale", 10: "Description", 13: "Revision"},
       [(11, "AuditEvent.ActorId: unit.created"), (12, "AuditEvent.OccurredAt: unit.created"), (14, "AuditEvent.OccurredAt: unit.edited / unit.status-changed")])
MAP["INV03"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["UnitTests.cs", "UnitSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV04", "GET/POST /companies/{companyId}/items; GET/PUT /{id}; POST /{id}/status; GET /{id}/audit-events|convert-quantity; GET /units/{id}/items",
       "بطاقة صنف ووحدات خاصة به ومعامل أساسي واحد وباركود فريد وسعر بالعملة الوظيفية؛ تحويل دقيق؛ حماية المجموعات والوحدات المستخدمة؛ بحث وحالة وتدقيق",
       "قوائم نوع الصنف والتتبع نطاق أولي مستنتج من الغرض وINV05 وليست قائمة سياسة نهائية؛ الضريبة وحركة المخزون والدفعات والتسلسل التشغيلي والاستخدام المستندي غير منفذة؛ قفل الصنف بعد الحركة يجب إضافته مع دفتر المخزون",
       {1: "Item.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "Type", 9: "GroupId", 10: "BaseUnitId", 11: "Tracking", 12: "ShelfLifeDays", 13: "ReorderLevel", 15: "Units[].UnitId", 16: "Units[].FactorToBase", 17: "Units[].Barcode", 18: "Units[].SalePrice (functional currency)", 21: "Revision"},
       [(19, "AuditEvent.ActorId: item.created"), (20, "AuditEvent.OccurredAt: item.created"), (22, "AuditEvent.OccurredAt: item.edited / item.status-changed")])
MAP["INV04"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["ItemTests.cs", "ItemSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("CASH01", "GET/POST /companies/{companyId}/cash-boxes; GET /search|/{id}|/{id}/usage|/{id}/audit-events; PUT /{id}; POST /{id}/status",
       "بطاقة صندوق بنطاق الشركة والفرع، حساب تفصيلي فعال وعملة وأمين عضوية نشطة؛ حقول 16 كاملة وإصدار/توقيت خادم؛ إنشاء وتعديل وتعطيل وبحث واستخدام قيد مرجعي وتدقيق ذري وتكرار آمن",
       "لا يوجد رصيد صندوق متاح أو منع سحب على مستوى CASH04؛ عرض الاستخدام يحصي القيود المرجعية لا الرصيد؛ صلاحيات الصندوق المستقلة وسياسة التشغيل وتكامل سندات القبض والصرف لم تنفذ؛ شاشة CASH01 جزئية",
       {1: "CashBox.Id: server GUID", 2: "CashBox.CompanyId from route", 3: "Code", 4: "Name",
        5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "AccountId: active posting account",
        9: "BranchId: active authorized branch", 10: "CustodianMembershipId: active membership in branch",
        11: "CurrencyCode: active company currency", 12: "BalanceLimit decimal(20,6)",
        13: "CreatedBy: server subject", 14: "CreatedAt: server UTC",
        15: "Revision: server integer", 16: "LastModifiedAt: server UTC"})
MAP["CASH01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["CashBoxTests.cs", "CashBoxSqlIntegration.cs", "SqlIntegration.cs", "OpenApiSnapshotTests.cs"])

screen("SYS16", "internal EF/SQL schema 033 only; public SYS16 payment API not enabled",
       "بنية أصلية لبطاقات طرق الدفع وخطط الأقساط: الشركة والفرع والرمز والاسم والوسيلة والحساب الاختياري وأيام السماح والنسب والاستحقاق والتدقيق؛ هجرة ذرية idempotent ومراجع SQL مركبة بالشركة؛ مجموع نسب خطة معطاة يساوي 100 في Domain، ورفض EF لأي كتابة غير معتمدة",
       "كتابة وقراءة SYS16 عبر API والبحث والتعديل والتعطيل والاستخدام وسجل التدقيق؛ اعتماد قائمة أنواع الوسائل التفصيلية وشروط إلزام الحساب، صلاحيات الفرع والحساب، ربط SAL06/PUR07/POS03 ومنع ترحيل البطاقة نقدًا؛ لا تعتبر البيانات schema-only وسيلة دفع تشغيلية",
       {})
for n, binding in {
    1: "PaymentTerm.CompanyId: tenant FK, schema only",
    2: "PaymentTerm.BranchId: nullable composite tenant FK, schema only",
    3: "PaymentTerm.IsActive: status column, writes blocked",
    4: "PaymentTerm.Code: nvarchar(32), unique within company",
    5: "PaymentTerm.Name: Unicode nvarchar(200)",
    6: "PaymentTerm.Kind: Cash/Card/Credit source-named initial means, no approved UI catalog",
    7: "PaymentTerm.SettlementAccountId: nullable tenant FK, conditional rules pending",
    8: "PaymentTerm.GraceDays: nonnegative int",
    9: "PaymentTermLine.Percent: decimal(9,6); domain sum exactly 100 if present; EF writes blocked",
    10: "PaymentTermLine.DaysFromBase: nonnegative int",
    11: "PaymentTerm.CreatedBy: internal server-owned actor, no API yet",
    12: "PaymentTerm.CreatedAt: internal datetimeoffset; new card rejects default audit time; no API yet",
    13: "PaymentTerm.Revision: positive int, no API yet",
    14: "PaymentTerm.LastModifiedAt: internal datetimeoffset, no API yet"
}.items():
    MAP["SYS16"]["fields"][f"SYS16-F{n:02}"] = dict(status="partial", binding=binding)
MAP["SYS16"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/"+name for name in
    ["PaymentTermFoundationTests.cs", "PaymentTermSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH02", "internal EF/SQL schema 032 only; no CASH02 public API",
       "بنية بطاقة بنك تحمل حقول CASH02-F01..F17، ومفاتيح مركبة للشركة والحساب والفرع والعملة وIBAN اختياري؛ منع تعديل البيانات البنكية قبل اعتماد الصلاحيات ومراجعة تغيير المستفيدين؛ ترقية ذرية قابلة للإعادة",
       "لا يمكن إنشاء/تحرير حساب بنكي عبر API حتى اعتماد التحقق الشرطي لوجوب IBAN وبيانات المستفيدين ومسار المراجعة؛ لا دفتر تسوية أو CASH07/CASH10 أو استعمال تشغيلي للبطاقة؛ الحقول بنية مخزنة فقط",
       {})
for n, binding in {
    1:"BankAccount.Id: internal GUID only", 2:"BankAccount.CompanyId: tenant key only",
    3:"BankAccount.Code: nvarchar(32) unique per company", 4:"BankAccount.Name: nvarchar(200)",
    5:"BankAccount.EnglishName: nullable", 6:"BankAccount.IsActive: blocked writes",
    7:"BankAccount.Notes: nullable", 8:"BankAccount.BankName: nvarchar(200)",
    9:"BankAccount.AccountNumber: nvarchar(64), preserves leading zeros",
    10:"BankAccount.Iban: nullable canonical IBAN; conditional country requirement not implemented",
    11:"BankAccount.LedgerAccountId: tenant FK", 12:"BankAccount.CurrencyCode: tenant FK",
    13:"BankAccount.BranchId: tenant FK", 14:"BankAccount.CreatedBy: internal actor",
    15:"BankAccount.CreatedAt: datetimeoffset", 16:"BankAccount.Revision: positive int",
    17:"BankAccount.LastModifiedAt: datetimeoffset"
}.items():
    MAP["CASH02"]["fields"][f"CASH02-F{n:02}"] = dict(status="partial", binding=binding)
MAP["CASH02"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/"+name for name in
    ["BankAccountFoundationTests.cs", "BankAccountSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH03", "internal EF/SQL schema 034 only; no receipt/cheque/GL API or balance effect",
       "بنية سند قبض و24 حقلًا أصليًا محفوظة في CashReceipts/CashReceiptLines؛ تاريخا الوثيقة والأثر، سعر صرف decimal(20,10)، مبلغ/تخصيص/خصم decimal(20,6)، صيغة صندوق أو بنك شرطية بمفاتيح شركة مركبة؛ ترقية 034 ذرية قابلة للإعادة؛ كل كتابات EF للقبض/السطور محظورة",
       "SAL11 للذمم والفواتير ومرجع جهة القبض غير موجود فعليًا؛ لا FK مخترع لهما. سياسة طريقة التحصيل والمصدر الشرطي والتفويض والترحيل وحالات الشيك وحجز الرصيد والتزامن SYS07/SYS03/GL06/CASH07 غير معتمدة؛ لا API قبض ولا حساب أثر مالي، والمستند البنيوي لا يسمح بحفظه",
       {})
for n, binding in {
    1:"CashReceipt.Id internal GUID; no public document/number allocation",
    2:"CashReceipt.CompanyId tenant primary key",
    3:"CashReceipt.BranchId tenant-scoped FK; operational authorization pending",
    4:"CashReceipt.FiscalYearId tenant FK; no fiscal period validation",
    5:"CashReceipt.Number nvarchar(64), preserves leading zeros; SYS10 sequence absent",
    6:"CashReceipt.DocumentDate SQL date",
    7:"CashReceipt.AccountingDate SQL date; no period/close enforcement yet",
    8:"CashReceipt.CurrencyCode tenant-scoped FK",
    9:"CashReceipt.ExchangeRate decimal(20,10)>0; no dated SYS05 rate lookup",
    10:"CashReceipt.Description nvarchar(1000)",
    11:"CashReceipt.ExternalReference nvarchar(100) nullable",
    12:"CashReceipt.State nonempty text; lifecycle not approved",
    13:"CashReceipt.PayerId GUID stored only; SAL11 payer FK unavailable",
    14:"CashReceipt.PaymentMethod nonempty text; approved method enum unavailable",
    15:"CashReceipt.CashBoxId / BankAccountId nullable tenant FKs; method/target binding unavailable",
    16:"CashReceipt.Amount decimal(20,6)>0; not posted",
    17:"CashReceipt.ChequeReference nvarchar(100) nullable; CASH07 link pending",
    18:"CashReceiptLine.InvoiceId optional GUID stored only; SAL11 invoice FK unavailable",
    19:"CashReceiptLine.AllocatedAmount decimal(20,6)>=0; balance check not live",
    20:"CashReceiptLine.SettlementDiscount decimal(20,6)>=0; no applied discount posting",
    21:"CashReceipt.CreatedBy stored actor text; no operational auditing",
    22:"CashReceipt.CreatedAt datetimeoffset(7) stored; no operational stamping",
    23:"CashReceipt.Revision positive int; no edit API",
    24:"CashReceipt.LastModifiedAt datetimeoffset(7)>=CreatedAt; no edit API"
}.items():
    MAP["CASH03"]["fields"][f"CASH03-F{n:02}"] = dict(status="partial", binding=binding)
MAP["CASH03"]["test_file"] = "; ".join(
    "backend/tests/Ameen.Erp.Tests/" + name for name in
    ["CashReceiptAllocationTests.cs", "CashReceiptFoundationTests.cs",
     "CashReceiptSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH06", "internal EF/SQL schema 039 only; no live advance/settlement API or GL posting",
       "بنية 23 حقلًا أصليًا لعنوان وسطور العهد والسلف التشغيلية: جداول SQL 039 وEF مع مبالغ دقيقة وسعر صرف وفحص مراجع الشركة والفرع والسنة والعملة، وتسجيل وقت/إصدار تدقيق، ومنع الكتابة تشغيلًا",
       "HR03 صاحب العهدة والمصروف الفعلي غير موصولين، ولا قائمة أنواع/حالات العهدة المعتمدة ولا سياسة رد النقد أو الزيادة أو تسوية ذمم GL06؛ كل EF writes محجوبة، وCASH06 API/HTTPS/JWT/Outbox/موافقات غير منفذة",
       {})
for n, binding in {
    1:"CashAdvance.Id internal GUID only",
    2:"CashAdvance.CompanyId tenant primary key",
    3:"CashAdvance.BranchId tenant composite FK; operational permission pending",
    4:"CashAdvance.FiscalYearId tenant FK; fiscal closure not checked",
    5:"CashAdvance.Number nvarchar(64) preserves leading zeroes; no SYS10 scope",
    6:"CashAdvance.DocumentDate SQL date",
    7:"CashAdvance.AccountingDate SQL date; not posted",
    8:"CashAdvance.CurrencyCode tenant composite FK",
    9:"CashAdvance.ExchangeRate decimal(20,10)>0; no dated SYS05 policy",
    10:"CashAdvance.Description nvarchar(1000)",
    11:"CashAdvance.ExternalReference nullable nvarchar(100)",
    12:"CashAdvance.State nonempty text; approved lifecycle missing",
    13:"CashAdvance.HolderId GUID stored only; HR03 master FK unavailable",
    14:"CashAdvance.AdvanceType nonempty text; approved choices unavailable",
    15:"CashAdvance.Amount decimal(20,6)>0; no cash debit",
    16:"CashAdvance.ExpectedSettlementDate SQL date; no workflow timing",
    17:"CashAdvanceLine.ExpenseReferenceId nullable GUID, no approved expense FK",
    18:"CashAdvanceLine.SettlementAmount nullable decimal(20,6)>=0, no real settlement",
    19:"CashAdvanceLine.AttachmentReference nullable nvarchar(300)",
    20:"CashAdvance.CreatedBy stored actor text; API actor validation pending",
    21:"CashAdvance.CreatedAt datetimeoffset(7) stored only",
    22:"CashAdvance.Revision >0 SQL, no operational edit",
    23:"CashAdvance.LastModifiedAt datetimeoffset(7)>=CreatedAt, no audit workflow"
}.items():
    MAP["CASH06"]["fields"][f"CASH06-F{n:02}"] = dict(status="partial", binding=binding)
MAP["CASH06"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + x for x in
    ["CashAdvanceFoundationTests.cs", "CashAdvanceSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH07", "internal EF/SQL schema 035 only; no cheque receipt, clearing, rejection or bank API",
       "بنية الشيك الوارد 035 تخزن 22 حقلًا أصليًا بدقة decimal(20,6) وdecimal(20,10)، ورقم شيك بأصفار بادئة؛ فرع وسنة وعملة وحساب تحت التحصيل بمفاتيح مركبة للشركة؛ EF يمنع جميع كتابات الشيك، ترقية ذرية قابلة للإعادة واختبارات SQL مؤقتة",
       "CASH03 القبض والتحصيل وCASH09 دورة حالات الشيك وGL06 الترحيل والذمة وسداد/إعادة دين الدافع غير متصلة؛ فريد بنك/دافع وفق سياسة غير محسومة، لا unique FK مفترض للدافع ولا حركات بنك ولا API/JWT تشغيلية",
       {})
MAP["CASH07"]["fields"]["CASH07-F01"] = dict(status="partial", binding='IncomingCheque.Id internal GUID, no business number allocation')
MAP["CASH07"]["fields"]["CASH07-F02"] = dict(status="partial", binding='IncomingCheque.CompanyId tenant key')
MAP["CASH07"]["fields"]["CASH07-F03"] = dict(status="partial", binding='IncomingCheque.BranchId tenant composite FK; operational branch permission pending')
MAP["CASH07"]["fields"]["CASH07-F04"] = dict(status="partial", binding='IncomingCheque.FiscalYearId tenant composite FK; period posting pending')
MAP["CASH07"]["fields"]["CASH07-F05"] = dict(status="partial", binding='IncomingCheque.Number nvarchar(64) preserves leading zeroes, SYS10 numbering not linked')
MAP["CASH07"]["fields"]["CASH07-F06"] = dict(status="partial", binding='IncomingCheque.DocumentDate SQL date')
MAP["CASH07"]["fields"]["CASH07-F07"] = dict(status="partial", binding='IncomingCheque.AccountingDate SQL date; period guard pending')
MAP["CASH07"]["fields"]["CASH07-F08"] = dict(status="partial", binding='IncomingCheque.CurrencyCode tenant composite FK')
MAP["CASH07"]["fields"]["CASH07-F09"] = dict(status="partial", binding='IncomingCheque.ExchangeRate decimal(20,10), positive; dated rate policy pending')
MAP["CASH07"]["fields"]["CASH07-F10"] = dict(status="partial", binding='IncomingCheque.Description nvarchar(1000)')
MAP["CASH07"]["fields"]["CASH07-F11"] = dict(status="partial", binding='IncomingCheque.ExternalReference nullable nvarchar(100)')
MAP["CASH07"]["fields"]["CASH07-F12"] = dict(status="partial", binding='IncomingCheque.State nonempty text only; approved state graph pending')
MAP["CASH07"]["fields"]["CASH07-F13"] = dict(status="partial", binding='IncomingCheque.ChequeNumber nvarchar(64), preserves leading zeros; uniqueness scope unresolved')
MAP["CASH07"]["fields"]["CASH07-F14"] = dict(status="partial", binding='IncomingCheque.DrawnBankName nvarchar(200); no approved bank identity FK')
MAP["CASH07"]["fields"]["CASH07-F15"] = dict(status="partial", binding='IncomingCheque.PayerId GUID only; party/master FK not available')
MAP["CASH07"]["fields"]["CASH07-F16"] = dict(status="partial", binding='IncomingCheque.Amount decimal(20,6)>0; no bank credit')
MAP["CASH07"]["fields"]["CASH07-F17"] = dict(status="partial", binding='IncomingCheque.DueDate SQL date')
MAP["CASH07"]["fields"]["CASH07-F18"] = dict(status="partial", binding='IncomingCheque.CollectionAccountId tenant composite FK Accounts; not posted')
MAP["CASH07"]["fields"]["CASH07-F19"] = dict(status="partial", binding='IncomingCheque.CreatedBy text stored only; server actor and audit pending')
MAP["CASH07"]["fields"]["CASH07-F20"] = dict(status="partial", binding='IncomingCheque.CreatedAt datetimeoffset(7) stored only')
MAP["CASH07"]["fields"]["CASH07-F21"] = dict(status="partial", binding='IncomingCheque.Revision >0 SQL; no edit API')
MAP["CASH07"]["fields"]["CASH07-F22"] = dict(status="partial", binding='IncomingCheque.LastModifiedAt datetimeoffset(7)>=CreatedAt; no edit API')
MAP["CASH07"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + x for x in ["ChequeFoundationTests.cs", "ChequeSqlIntegration.cs", "SqlIntegration.cs"])
screen("CASH08", "internal EF/SQL schema 036 only; no outbound cheque posting, cancellation or bank debit",
       "بنية الشيك الصادر 036 تخزن 22 حقلًا أصليًا، مفتاح بنك مرتبط بالشركة، رقم الشيك نصي بالأصفار البادئة وحالة شيك نصية بنيوية دون مراحل معتمدة؛ دقة decimal(20,6)/decimal(20,10) وعزل الشركة؛ EF يمنع الكتابة",
       "CASH04 سند صرف وCASH09 انتقالات شيك وCASH10 مطابقة الكشف وSYS07 التفويض وGL06 الترحيل غير متصلة؛ منع إعادة الرقم الملغى يتطلب نطاق دفتر شيكات/تسلسل معتمد قبل اعتماد فهرس فريد، والمستفيد مخزن كمعرف دون FK معتمدة، لا API تشغيلي",
       {})
MAP["CASH08"]["fields"]["CASH08-F01"] = dict(status="partial", binding='OutgoingCheque.Id internal GUID')
MAP["CASH08"]["fields"]["CASH08-F02"] = dict(status="partial", binding='OutgoingCheque.CompanyId tenant key')
MAP["CASH08"]["fields"]["CASH08-F03"] = dict(status="partial", binding='OutgoingCheque.BranchId tenant composite FK')
MAP["CASH08"]["fields"]["CASH08-F04"] = dict(status="partial", binding='OutgoingCheque.FiscalYearId tenant composite FK')
MAP["CASH08"]["fields"]["CASH08-F05"] = dict(status="partial", binding='OutgoingCheque.Number nvarchar(64) preserves leading zeros; SYS10 not linked')
MAP["CASH08"]["fields"]["CASH08-F06"] = dict(status="partial", binding='OutgoingCheque.DocumentDate SQL date')
MAP["CASH08"]["fields"]["CASH08-F07"] = dict(status="partial", binding='OutgoingCheque.AccountingDate SQL date; no fiscal posting')
MAP["CASH08"]["fields"]["CASH08-F08"] = dict(status="partial", binding='OutgoingCheque.CurrencyCode tenant composite FK')
MAP["CASH08"]["fields"]["CASH08-F09"] = dict(status="partial", binding='OutgoingCheque.ExchangeRate decimal(20,10)>0; no approved FX policy')
MAP["CASH08"]["fields"]["CASH08-F10"] = dict(status="partial", binding='OutgoingCheque.Description nvarchar(1000)')
MAP["CASH08"]["fields"]["CASH08-F11"] = dict(status="partial", binding='OutgoingCheque.ExternalReference nullable nvarchar(100)')
MAP["CASH08"]["fields"]["CASH08-F12"] = dict(status="partial", binding='OutgoingCheque.State nonempty stored text only')
MAP["CASH08"]["fields"]["CASH08-F13"] = dict(status="partial", binding='OutgoingCheque.ChequeNumber nvarchar(64) preserves leading zeros; cancelled number reuse policy pending')
MAP["CASH08"]["fields"]["CASH08-F14"] = dict(status="partial", binding='OutgoingCheque.BankAccountId tenant composite FK; bank writes blocked')
MAP["CASH08"]["fields"]["CASH08-F15"] = dict(status="partial", binding='OutgoingCheque.BeneficiaryId GUID stored only; beneficiary master FK not approved')
MAP["CASH08"]["fields"]["CASH08-F16"] = dict(status="partial", binding='OutgoingCheque.Amount decimal(20,6)>0; no outgoing bank debit')
MAP["CASH08"]["fields"]["CASH08-F17"] = dict(status="partial", binding='OutgoingCheque.DueDate SQL date')
MAP["CASH08"]["fields"]["CASH08-F18"] = dict(status="partial", binding='OutgoingCheque.ChequeState nonempty text only; transitions not approved')
MAP["CASH08"]["fields"]["CASH08-F19"] = dict(status="partial", binding='OutgoingCheque.CreatedBy stored text only')
MAP["CASH08"]["fields"]["CASH08-F20"] = dict(status="partial", binding='OutgoingCheque.CreatedAt datetimeoffset(7) stored only')
MAP["CASH08"]["fields"]["CASH08-F21"] = dict(status="partial", binding='OutgoingCheque.Revision >0; no edit API')
MAP["CASH08"]["fields"]["CASH08-F22"] = dict(status="partial", binding='OutgoingCheque.LastModifiedAt datetimeoffset(7)>=CreatedAt; no edit API')
MAP["CASH08"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + x for x in ["ChequeFoundationTests.cs", "ChequeSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH09", "internal EF/SQL schema 037 only; no cheque clearing/cancellation API",
       "بنية حركة شيك 037 تمثل 13 حقلًا أصليًا مع حل مرجع الشيك ذي النوعين عبر IncomingChequeId أو OutgoingChequeId بمفتاح شركة مركب وقيد exactly-one، ومرجع بنك مستلم اختياري وعمولة decimal(20,6) غير سالبة؛ ترقية ذرية قابلة للإعادة وحارس EF يمنع كل كتابة",
       "قائمة أفعال وانتقالات وحالات CASH09 لم تعتمد؛ لا تحصيل شيك مرتين أو رفض/إلغاء أو إعادة الذمة قبل اعتماد CAS/GL06/SYS07 وCASH07/CASH08 وقواعد التزامن، ولا API تحصيل أو سجل تغير فعلي؛ SQL محفوظ بنيويًا فقط",
       {})
MAP["CASH09"]["fields"]["CASH09-F01"] = dict(status="partial", binding='ChequeMovement.CompanyId tenant primary key and FK; no public API')
MAP["CASH09"]["fields"]["CASH09-F02"] = dict(status="partial", binding='ChequeMovement.BranchId nullable tenant scoped FK')
MAP["CASH09"]["fields"]["CASH09-F03"] = dict(status="partial", binding='ChequeMovement.State nonempty text; no approved state transitions')
MAP["CASH09"]["fields"]["CASH09-F04"] = dict(status="partial", binding='ChequeMovement.ChequeId computed from exactly one IncomingChequeId or OutgoingChequeId; both are tenant FKs')
MAP["CASH09"]["fields"]["CASH09-F05"] = dict(status="partial", binding='ChequeMovement.Action nonempty text, no approved action set, EF writes blocked')
MAP["CASH09"]["fields"]["CASH09-F06"] = dict(status="partial", binding='ChequeMovement.ActionDate SQL date; no effective financial date validation')
MAP["CASH09"]["fields"]["CASH09-F07"] = dict(status="partial", binding='ChequeMovement.ReceivedBankAccountId nullable company FK; no bank credit')
MAP["CASH09"]["fields"]["CASH09-F08"] = dict(status="partial", binding='ChequeMovement.RejectionReason optional nvarchar(1000); return receivable not implemented')
MAP["CASH09"]["fields"]["CASH09-F09"] = dict(status="partial", binding='ChequeMovement.Fee nullable decimal(20,6)>=0; no commission journal')
MAP["CASH09"]["fields"]["CASH09-F10"] = dict(status="partial", binding='ChequeMovement.CreatedBy actor stored only; no operation authorized')
MAP["CASH09"]["fields"]["CASH09-F11"] = dict(status="partial", binding='ChequeMovement.CreatedAt datetimeoffset(7) stored')
MAP["CASH09"]["fields"]["CASH09-F12"] = dict(status="partial", binding='ChequeMovement.Revision int>0; no edit API or CAS transaction')
MAP["CASH09"]["fields"]["CASH09-F13"] = dict(status="partial", binding='ChequeMovement.LastModifiedAt datetimeoffset(7)>=CreatedAt; audit events pending')
MAP["CASH09"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + x for x in ["ChequeMovementFoundationTests.cs", "ChequeMovementSqlIntegration.cs", "ChequeSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH10", "internal EF/SQL schema 040 only; no bank statement import or reconciliation API",
       "بنية 17 حقلًا أصليًا لرأس ومحضر المطابقة وسطور حركات البنك والدفتر؛ SQL 040 ذرية قابلة للإعادة، ربط حساب البنك والفرع والشركة بمفاتيح مركبة، ومبالغ decimal(20,6) تشمل رصيد كشف سالبًا مشروعًا؛ EF يمنع كل كتابة حتى اعتماد ربط الحركات وسياسة الاستيراد",
       "CASH02 بطاقة البنك تشغيلها محجوب؛ لا مصدر حركة كشف أو حركة دفتر مثبت للربط، ولا مفتاح استيراد فريد معتمد، ولا سياسات التوزيع الجزئي والمتعدد/الفرق أو مسودة عمولة GL03 أو التفويض SYS07؛ CASH10 API/JWT/SQL transactional matching غير منفذة",
       {})
for n, binding in {
    1:"BankReconciliation.CompanyId tenant key",
    2:"BankReconciliation.BranchId optional composite tenant FK; operational scope pending",
    3:"BankReconciliation.State nonempty text; lifecycle not approved",
    4:"BankReconciliation.BankAccountId composite tenant FK; bank CRUD blocked",
    5:"BankReconciliation.StatementFrom SQL date",
    6:"BankReconciliation.StatementTo SQL date; >= StatementFrom",
    7:"BankReconciliation.OpeningBalance signed decimal(20,6); no ledger impact",
    8:"BankReconciliation.ClosingBalance signed decimal(20,6); no ledger impact",
    9:"BankReconciliation.StatementFileId nullable nvarchar(300); source import not approved",
    10:"BankReconciliationLine.BankMovementId GUID only; bank movement master FK unavailable",
    11:"BankReconciliationLine.LedgerMovementId optional GUID only; source FK unavailable",
    12:"BankReconciliationLine.MatchedAmount decimal(20,6)>=0; allocation policy pending",
    13:"BankReconciliationLine.VarianceReason optional nvarchar(1000); final variance not calculated",
    14:"BankReconciliation.CreatedBy text stored only; actor validation pending",
    15:"BankReconciliation.CreatedAt datetimeoffset(7) stored only",
    16:"BankReconciliation.Revision>0; no operational edit",
    17:"BankReconciliation.LastModifiedAt>=CreatedAt; no operational update"
}.items():
    MAP["CASH10"]["fields"][f"CASH10-F{n:02}"] = dict(status="partial", binding=binding)
MAP["CASH10"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + n for n in
    ["BankReconciliationFoundationTests.cs", "BankReconciliationSqlIntegration.cs", "SqlIntegration.cs"])

screen("CASH11", "internal EF/SQL schema 033 only; no CASH11 public API or balance adjustment",
       "بنية محضر جرد صندوق وسطور فئات/عدد القطع بقيمة Decimal محسوبة في SQL؛ مفتاح صندوق وفرع/قائم بالجرد ضمن الشركة وتدقيق زمني وإصدار؛ ترقية 033 ذرية قابلة للإعادة؛ كل الكتابات عبر EF محجوبة حتى اعتماد مسار الرصيد والتسوية",
       "لا توجد عملية جرد أو مقارنة رصيد دفتر أو محضر عجز/زيادة تشغيلي أو اعتماد تسوية GL03؛ الحالة المسموحة وقواعد تفعيل الجرد وصلاحيات SYS07 تتطلب مسارًا معتمدًا؛ SQL بنيوي فقط، لا HTTP/JWT/OpenAPI عمليات CASH11",
       {})
for n, binding in {
    1:"CashCount.CompanyId: tenant primary key and SQL FK; no API",
    2:"CashCount.BranchId: nullable tenant FK; no branch operation permission yet",
    3:"CashCount.State: text slot with nonempty SQL check; lifecycle values not approved; EF writes blocked",
    4:"CashCount.CashBoxId: tenant composite FK; no balance lookup",
    5:"CashCount.CutoffAt: datetimeoffset(7); no ledger snapshot",
    6:"CashCount.CountedByMembershipId: tenant FK; active membership/permission blocked",
    7:"CashCountLine.Denomination: decimal(20,6) >0; no currency denominations catalogue",
    8:"CashCountLine.PieceCount: nonnegative int",
    9:"CashCountLine.CountedValue: SQL persisted computed decimal(20,6)",
    10:"CashCount.CreatedBy: stored only; no operational subject validation",
    11:"CashCount.CreatedAt: datetimeoffset(7) stored only",
    12:"CashCount.Revision: positive server integer structural only; no edit API",
    13:"CashCount.LastModifiedAt: datetimeoffset(7) >= CreatedAt; no edit API"
}.items():
    MAP["CASH11"]["fields"][f"CASH11-F{n:02}"] = dict(status="partial", binding=binding)
MAP["CASH11"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/"+name
    for name in ["CashCountFoundationTests.cs", "CashCountSqlIntegration.cs", "SqlIntegration.cs"])

screen("RPT01", "GET /companies/{companyId}/reports/trial-balance",
       "أول المدة والحركة وآخرها بالوظيفية؛ تجميع مستوى الدليل ومرشح المركز وإخفاء الصفري دون ازدواج", "طرق عرض العملات ومستوى التفصيل والطباعة والتصدير وتحسين أحجام كبيرة",
       {1: "CompanyId", 2: "BranchIds", 3: "query.from", 4: "query.to", 9: "query.accountLevel (null = posting leaves; 1..20 = maximum displayed depth)", 10: "query.costCenterId", 11: "query.showZero"})
screen("RPT02", "GET /companies/{companyId}/reports/accounts/{accountId}/ledger",
       "حساب واحد برصيد افتتاحي وجارٍ صحيح عبر الصفحات؛ مرشح مركز ومرجع خارجي يطبقان على الافتتاح والحركة", "نطاق حسابات وتصدير وطباعة وطرق عرض إضافية",
       {1: "CompanyId", 2: "BranchIds", 3: "query.from", 4: "query.to", 11: "query.costCenterId", 12: "query.externalReference (exact case-sensitive match)"})

screen("RPT08", "GET /companies/{companyId}/reports/inventory-balances",
       "رصيد وحركة وقيمة افتتاح المخزون وعكسه حسب المخزن والصنف والهوية والرقابة؛ إجماليات قبل الصفحات وعزل الفروع",
       "افتتاح وعكسه فقط؛ لا حجز/متاح/طريق/عزل ولا محرك تكلفة أو مطابقة رقابة آلية عامة؛ لا عملة عرض أو طباعة أو تصدير؛ حتى تاريخ يستخدم to نفسه؛ التجميع ثابت",
       {1: "CompanyId", 2: "BranchIds: authorized scope; optional branchId", 3: "query.from", 4: "query.to", 10: "query.warehouseId", 11: "query.groupId (current item group)", 12: "query.itemId"})
screen("RPT09", "GET /companies/{companyId}/reports/items/{itemId}/ledger",
       "حركة افتتاح وعكس بوحدة أساسية ورصيد جارٍ عبر الصفحات؛ وحدات وتكلفة سطور المصدر التاريخية وروابط المستند والقيد؛ ترتيب تاريخ الأثر ووقت الترحيل",
       "المصادر الحالية افتتاح وعكسه فقط؛ لا صرف/استلام/تحويل؛ لا عملة عرض أو طباعة أو تصدير؛ التفصيل ثابت؛ تجميع القراءة في الذاكرة يحتاج تحسينًا للأحجام الكبيرة",
       {1: "CompanyId", 2: "BranchIds: authorized scope; optional branchId", 3: "query.from", 4: "query.to", 9: "route.itemId", 10: "query.warehouseId", 11: "query.identityNumber (exact normalized lot or serial number)"})
for report in ["RPT08", "RPT09"]:
    MAP[report]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["InventoryReportTests.cs", "InventoryOpeningSqlIntegration.cs", "HostedApiIntegration.cs"])

# Partial/derived semantics are explicit, never counted as full field implementation.
for code, n, binding, status in [
    ("SYS03", 10, "FiscalPeriod.IsClosed: general ledger only", "partial"),
    ("SYS05", 5, "Company.FunctionalCurrency", "derived"),
    ("SYS06", 6, "Membership.IsActive (ERP membership, not identity account)", "partial"),
    ("SYS06", 12, "Membership.IsActive (ERP membership only)", "partial"),
    ("SYS09", 8, "Company.RequireSeparateApprover always true; no configuration API", "partial"),
    ("GL03", 4, "FiscalPeriod.FiscalYearId", "derived"),
    ("GL03", 8, "currency is on each line; no single document currency", "partial"),
    ("GL03", 9, "rate is on each line; no single document rate", "partial"),
    ("GL03", 13, "Source: Manual / Recurring / Reversal provenance; configurable journal-type catalog is pending", "partial"),
    ("GL07", 4, "FiscalPeriod.FiscalYearId", "derived"),
    ("GL07", 8, "Lines[].CurrencyCode copied from original", "partial"),
    ("GL07", 9, "Lines[].ExchangeRate copied from original", "partial"),
    ("INV04", 8, "Item.Type: Goods/Service/Material/Product initial inferred scope", "partial"),
    ("INV04", 11, "Item.Tracking: None/Lot/Serial initial inferred scope", "partial"),
    ("INV04", 18, "Item.Units[].SalePrice: functional currency only", "partial"),
    ("GL11", 3, "FiscalPeriod.IsClosed: general ledger only", "partial"),
    ("GL11", 7, "PeriodClosePreview.Checks.Code: general ledger checks only", "partial"),
    ("GL11", 8, "PeriodClosePreview.Checks.Passed: general ledger checks only", "partial"),
    ("RPT08", 5, "Currency: functional currency only", "partial"),
    ("RPT08", 7, "StockMovement: posted openings and reversals only", "partial"),
    ("RPT08", 9, "query.to: shared inclusive cutoff, no separate third date", "partial"),
    ("RPT09", 5, "Currency: functional currency only", "partial"),
    ("RPT09", 7, "StockMovement: posted openings and reversals only", "partial"),
    ("RPT01", 5, "Currency: functional currency only; no selection", "partial"),
    ("RPT01", 7, "Posted and Reversed originals only; no selection", "partial"),
    ("RPT02", 5, "Currency: functional currency; original line amounts retained", "partial"),
    ("RPT02", 7, "Posted and Reversed originals only", "partial"),
    ("RPT02", 9, "route.accountId: one account only", "partial"),
]:
    MAP[code]["fields"][f"{code}-F{n:02}"] = dict(status=status, binding=binding)

RULES = {
    "BR01": ("manual-journal", "Decimal واتزان دقيق؛ رفض فقدان الدقة؛ فروق التقريب تحتاج سطرًا صريحًا", "Program.cs: BR01 / Wire contract"),
    "BR02": ("partial", "شركة وفرع وحساب ومركز وعملة وسعر ووحدة ومجموعة وصنف؛ مخازن ومواقع؛ لا أطراف أو ضرائب بعد", "Program.cs: BR02; SqlIntegration.cs: company isolation"),
    "BR03": ("manual-journal", "تاريخ الوثيقة والأثر ووقت الترحيل مستقلة؛ فترة مفتوحة", "Program.cs: BR03"),
    "BR04": ("partial", "حساب فعال فرعي وعملة ومركز صحيح؛ المشروع والطرف لاحقًا", "Program.cs: BR04"),
    "BR05": ("partial", "ذرية القيد والتدقيق وOutbox والنتيجة والتوليد الدوري وافتتاح المخزون؛ بقية الدفاتر والعامل الخارجي لم تنفذ", "Program.cs: BR05; SqlIntegration.cs / RecurringSqlIntegration.cs: constraint rollback"),
    "BR06": ("partial", "Idempotency per company/actor/operation/body؛ قيد دوري فريد لكل قالب وفترة؛ مصادر بقية المستندات لاحقًا", "Program.cs: BR06; RecurringTests.cs; SqlIntegration.cs / RecurringSqlIntegration.cs: concurrency"),
    "BR07": ("partial", "حماية المرحل وعكس مرتبط تاريخي للقيد وافتتاح المخزون بكمية وقيمة وقيد ذري؛ بقية المصادر لاحقًا", "Program.cs: BR07; InventoryOpeningReversalTests.cs; InventoryOpeningSqlIntegration.cs"),
    "BR08": ("partial", "منع قيد يدوي للحساب الرقابي؛ مطابقة افتتاح المخزون وحركته مع قيده عند الإقفال؛ بقية الدفاتر لاحقًا", "Program.cs: BR08"),
    "BR09": ("partial", "تدقيق الأوامر وفصل المنشئ والمعتمد؛ مسارات مراجعة متعددة لاحقًا", "Program.cs: BR09 / Workflow"),
    "BR10": ("manual-journal", "سعر موجب مؤرخ ثابت ومبالغ أصلية ووظيفية وعكس تاريخي", "Program.cs: BR10"),
    "BR14": ("partial", "منع الإقفال مع قيود/افتتاحات معلقة أو افتتاح مخزون غير مطابق لقيده؛ الإقفال الشامل والسنوي لاحقًا", "Program.cs: BR03; SqlIntegration.cs: close/post race"),
    "BR15": ("partial", "تقارير الأستاذ تستبعد المسودات؛ لا تقارير ذمم منفذة", "Program.cs: RPT01 RPT02"),
    "BR18": ("partial", "عزل الشركات والفروع وعضويات حالية؛ نطاق الحساب والسعر والتصدير لاحقًا", "Program.cs: BR18 / SYS06"),
    "BR20": ("not-started", "لم تنفذ ضرائب أو ملفات تشريعية أو تكاملات دولة", "none"),
}


def csv_text(headers, rows):
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\n")
    writer.writerow(headers)
    writer.writerows(rows)
    return stream.getvalue()


def generate():
    screens = json.loads((ANALYSIS / "data/screens.json").read_text())
    diagrams = json.loads((ANALYSIS / "data/diagrams.json").read_text())
    assert len(screens) == 150 and len(diagrams) == 28
    assert set(MAP).issubset({s["id"] for s in screens})
    field_rows, screen_rows = [], []
    for s in screens:
        mapping = MAP.get(s["id"], {})
        known = mapping.get("fields", {})
        assert set(known).issubset({f["id"] for f in s["fields"]}), s["id"]
        screen_rows.append([s["id"], s["module"], s["name"], "partial" if mapping else "not-started",
                            mapping.get("api", ""), mapping.get("implemented", ""), mapping.get("remaining", "الشاشة كاملة"),
                            mapping.get("test_file", "backend/tests/Ameen.Erp.Tests/Program.cs") if mapping else ""])
        for f in s["fields"]:
            found = known.get(f["id"], {})
            field_rows.append([s["id"], f["id"], f["name"], f["requirement"], found.get("status", "pending"), found.get("binding", ""), mapping.get("api", "") if found else ""])
    assert len(field_rows) == 2759 and len({r[1] for r in field_rows}) == 2759
    diagram_notes = {
        "module-SYS": "تهيئة أساسية فقط؛ راجع صفوف SYS في مصفوفة الشاشات",
        "module-GL": "دليل حسابات مع تحرير مضبوط وطبيعة صريحة وتدقيق؛ أستاذ وقوالب وتوليد مسودات؛ باقي وظائف GL غير مكتملة",
        "module-INV": "وحدات INV03 ومجموعات INV02 وبطاقات INV04 وتحويلات وباركود وحماية المراجع وعرض الاستخدام؛ بطاقات مخازن INV01 ومواقعها؛ الحركات والتكلفة والتتبع التشغيلي لم تنفذ",
        "module-COST": "شجرة مراكز ومسؤول وتعديل وتعطيل؛ نوع المركز والموازنات والتوزيعات لم تكتمل",
        "module-CASH": "CASH01 بطاقة صندوق بيانات وواجهات؛ CASH02 أساس بنكي بنيوي للحقول مع حظر كتابته؛ CASH03 أساس سند قبض بنيوي فقط مع حظر كتابته؛ CASH07/CASH08 أساس شيكات بنيوي فقط مع حظر كتابتها؛ CASH09 أساس حركة شيك بنيوي مع منع جميع الكتابات؛ CASH11 أساس جرد بنيوي فقط مع حظر الكتابة؛ الأرصدة والسندات والشيكات والمطابقة لم تنفذ",
        "module-RPT": "ميزان وأستاذ جزئيان",
        "system-map": "أساس SYS وGL وبعض COST وINV وRPT؛ بقية الأقسام لم تبدأ",
        "data-model": "شركات وفروع وحسابات وفترات وقيود وسطـور وتدقيق وOutbox؛ امتداد قوالب دورية بإصدار وتوليد ووحدات ومجموعات وITEM/ITEM_UNIT وWAREHOUSE/Locations؛ DOCUMENT والدفاتر المساعدة لاحقًا",
        "document-lifecycle": "الدورة منفذة للقيد اليدوي؛ الرفض يعيد Draft مع سبب وتاريخ في التدقيق؛ محاولة الترحيل ليست حالة دائمة",
        "posting-sequence": "ذرية القيد وOutbox والتدقيق؛ لا دفاتر مساعدة أو عامل إرسال خارجي بعد",
        "period-close": "فحص القيود المعلقة واتزانها فقط؛ باقي مسار المطابقة والإقفال السنوي غير منفذ",
    }
    outputs = {
        "screens-implementation.csv": csv_text(["screen_id", "module", "name", "status", "api", "implemented_scope", "remaining", "test_file"], screen_rows),
        "fields-implementation.csv": csv_text(["screen_id", "field_id", "name", "requirement", "status", "binding", "api"], field_rows),
        "diagrams-implementation.csv": csv_text(["diagram_id", "analysis_source", "status", "scope"], [
            [d["id"], "muhaseb-erp-analysis/" + d["mermaid"], "partial" if d["id"] in diagram_notes else "not-started", diagram_notes.get(d["id"], "لم يبدأ التنفيذ")]
            for d in diagrams]),
        "rules-implementation.csv": csv_text(["rule_id", "scope", "implemented_and_remaining", "evidence"], [
            [f"BR{i:02}", *RULES.get(f"BR{i:02}", ("not-started", "لم يبدأ التنفيذ", "none"))] for i in range(1, 21)]),
        "implementation-map.json": json.dumps({
            "analysis_screens_sha256": hashlib.sha256((ANALYSIS / "data/screens.json").read_bytes()).hexdigest(),
            "screen_count": len(screens), "field_count": len(field_rows), "diagram_count": len(diagrams),
            "partially_implemented_screens": len(MAP), "complete_screens": 0,
            "field_status_counts": {state: sum(r[4] == state for r in field_rows) for state in ["implemented", "audit", "derived", "partial", "pending"]},
            "coverage_note": "Field bindings are reviewed mappings, not proof of an end-to-end UI or production operation.",
            "screens": MAP,
        }, ensure_ascii=False, indent=2) + "\n",
    }
    return outputs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for item in json.loads((ANALYSIS / "manifest.json").read_text()):
        data = (ANALYSIS / item["path"]).read_bytes()
        if hashlib.sha256(data).hexdigest() != item["sha256"]:
            raise SystemExit("Analysis changed: " + item["path"])
    outputs = generate()
    OUT.mkdir(parents=True, exist_ok=True)
    for name, content in outputs.items():
        path = OUT / name
        if args.check:
            if not path.exists() or path.read_text() != content:
                import difflib
                actual = path.read_text() if path.exists() else ""
                lines = list(difflib.unified_diff(actual.splitlines(), content.splitlines(),
                    fromfile=str(path.relative_to(ROOT)) + " (tracked)", tofile="expected from analysis", lineterm=""))
                for line in lines[:70]:
                    print(line)
                raise SystemExit("Traceability stale: " + str(path.relative_to(ROOT)))
        else:
            path.write_text(content)
    print(f"PASS: analysis hashes; 150 screens; 2759 fields; 28 diagrams; 20 rules; {len(MAP)} partial screens; no full-screen completion claim.")


if __name__ == "__main__":
    main()
