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
MAP["SYS03"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["FiscalCalendarTests.cs", "FiscalCalendarSqlIntegration.cs", "HostedApiIntegration.cs"])
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
       {1: "Membership.Id", 2: "CompanyId", 16: "Version"})
screen("SYS07", "GET/POST /companies/{companyId}/memberships",
       "منح عمليات خادمية صريحة لمستخدم داخل شركة؛ منع تغيير المستخدم لصلاحياته", "كتالوج أدوار قابل للإدارة وحدود مبالغ وعملات وصلاحيات بقية الأقسام",
       {1: "CompanyId", 7: "GrantedPermissions[]", 13: "Version"}, [(11, "AuditEvent.ActorId"), (12, "AuditEvent.OccurredAt")])
screen("SYS08", "GET/POST /companies/{companyId}/memberships",
       "نطاق الشركة والفروع مطبق على الأوامر والتقارير", "نطاق الحساب والمخزن والصندوق والعملاء ومراكز التكلفة المستقل",
       {1: "CompanyId", 4: "Subject", 6: "AllBranches / BranchIds[]", 13: "Version"}, [(11, "AuditEvent.ActorId"), (12, "AuditEvent.OccurredAt")])
screen("SYS09", "internal: Company.RequireSeparateApprover; /journals/{id}/approve",
       "فصل إلزامي بين منشئ القيد ومعتمده", "شاشة إعداد المسارات متعددة المراحل والحدود والتنبيهات",
       {1: "CompanyId"})
screen("SYS10", "internal: NumberSequence; POST /companies/{companyId}/journals",
       "تسلسل JV لكل شركة وسنة داخل معاملة؛ لا إعادة استخدام للملغى", "واجهة إعداد البوادئ والتسلسلات لكل أنواع المستندات والفروع",
       {1: "CompanyId"})
screen("SYS14", "GET /companies/{companyId}/audit-events",
       "قراءة سجل أحداث غير قابل للتعديل عبر التطبيق مع هوية ووقت وبيانات التغيير", "مرشحات التاريخ والمستخدم ونوع الحدث والشاشة؛ تصدير؛ تدقيق عمليات القراءة",
       {1: "CompanyId", 9: "Action", 10: "ActorId"})
screen("GL01", "GET/POST /companies/{companyId}/accounts; GET/PUT /accounts/{id}; POST /status; GET /usage|audit-events",
       "بطاقة ثنائية اللغة وملاحظات وطبيعة مدينة/دائنة صريحة؛ تعديل وشجرة بلا دورات وقفل بنية المستخدم وسجل استخدام وتغييرات",
       "مسار تحويل معتمد لتغيير بنية الحساب المستخدم؛ سجل الاستخدام الحالي للأستاذ والقوالب فقط حتى تنفيذ الدفاتر المساعدة",
       {1: "Account.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ParentId", 9: "Type", 10: "IsPosting", 11: "NormalBalance: Debit / Credit (required on create/edit)", 12: "CurrencyCode", 13: "IsControl", 14: "RequiresCostCenter", 17: "Revision (server integer); Version is a separate UUID concurrency token"},
       [(15, "AuditEvent.ActorId: account.created"), (16, "AuditEvent.OccurredAt: account.created"), (18, "AuditEvent.OccurredAt: account.edited / account.status-changed; /audit-events")])
MAP["GL01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["AccountTests.cs", "AccountSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("GL03", "GET/POST /companies/{companyId}/journals; GET/PUT /journals/{id}",
       "قيد يدوي متعدد العملات والمراكز بدورة مستقلة وأرقام وسعر تاريخي", "المشروع والطرف والمرفقات وأنواع القيود والعكس المجدول وتعديل تواريخ المسودة",
       {1: "JournalEntry.Id", 2: "CompanyId", 3: "BranchId", 5: "Number", 6: "DocumentDate", 7: "AccountingDate", 10: "Description", 11: "ExternalReference", 12: "State", 16: "Lines[].AccountId", 17: "Lines[].Debit", 18: "Lines[].Credit", 19: "Lines[].CurrencyCode", 20: "Lines[].ExchangeRate (ExchangeRateId on input)", 21: "Lines[].CostCenterId", 24: "Lines[].Description", 25: "CreatedBy", 26: "CreatedAt", 27: "Version"},
       [(28, "AuditEvent.OccurredAt: journal.edited")])
screen("GL04", "GET/POST /companies/{companyId}/recurring-journals; GET/PUT /recurring-journals/{id}; POST /status|generate; GET /generations|schedule|audit-events",
       "قالب شهري أو ربع سنوي أو سنوي؛ توليد يدوي لمسودة واحدة لكل فترة؛ إصدار تاريخي ومواعيد ثابتة وأسعار نافذة؛ بحث وتعطيل وسجل استخدام وتغييرات",
       "طرق توليد مجدولة غير طريقة Manual الحالية؛ تخصيص دوريات غير الشهرية وربع السنوية والسنوية إذا اعتمدت ضمن خيارات الشاشة",
       {1: "CompanyId", 2: "BranchId", 3: "IsActive", 4: "Name", 5: "Frequency", 6: "StartsOn", 7: "EndsOn", 9: "Lines[].AccountId",
        10: "Lines[].Debit", 11: "Lines[].Credit", 12: "Lines[].CostCenterId", 13: "CreatedBy", 14: "CreatedAt", 15: "Version"},
       [(16, "AuditEvent.OccurredAt: recurring.edited / recurring.status-changed; scoped /audit-events")])
MAP["GL04"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["RecurringTests.cs", "RecurringSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("GL05", "POST /companies/{companyId}/journals/{id}/submit|approve|return",
       "تقديم واعتماد مستقل وإعادة مسببّة للمسودة", "قائمة مراجعة بمرشح الفترة ومراجع منفصل عن المعتمد ومراحل اعتماد متعددة",
       {1: "route.companyId", 2: "JournalEntry.BranchId", 3: "JournalEntry.State", 7: "JournalEntry.Number", 13: "Version"},
       [(6, "AuditEvent.ActorId: journal.approve / journal.return"), (8, "AuditEvent.Action / Details.after"), (9, "AuditEvent.Details.reason: journal.return"), (11, "AuditEvent.ActorId"), (12, "AuditEvent.OccurredAt")])
screen("GL06", "POST /companies/{companyId}/journals/{id}/post",
       "ترحيل قيد يدوي واحد ذري مع منع التكرار وOutbox", "مستندات المصدر والترحيل الجماعي ومعاينة نتائج الدفعات وعامل إرسال Outbox",
       {1: "route.companyId", 2: "JournalEntry.BranchId", 3: "State", 6: "PostedAt", 7: "route.id", 9: "CommandReceipt.ResourceId / Number", 12: "Version"},
       [(10, "AuditEvent.ActorId: journal.post"), (11, "AuditEvent.OccurredAt: journal.post")])
screen("GL07", "POST /companies/{companyId}/journals/{id}/reverse",
       "مسودة عكس مرتبطة ثم اعتماد وترحيل؛ نسخ المبالغ التاريخية حرفيًا", "عكس مستندات الدفاتر المساعدة وفحص التبعيات والضريبة؛ عكس تلقائي مجدول",
       {1: "JournalEntry.Id", 2: "CompanyId", 3: "BranchId", 5: "Number", 6: "DocumentDate", 7: "AccountingDate", 10: "Description", 11: "ExternalReference", 12: "State", 13: "ReversesJournalId", 14: "ReversalReason", 15: "AccountingDate", 16: "CreatedBy", 17: "CreatedAt", 18: "Version"})
screen("GL11", "POST /companies/{companyId}/periods/{id}/close; GET /companies/{companyId}/periods/{id}/close-preview",
       "معاينة إقفال الأستاذ بشروط مفصلة وموانع مقسمة؛ تنفيذ يعيد فحص القيود المعلقة واتزان المرحل داخل المعاملة", "إقفال ERP شامل ومطابقة بنوك وذمم ومخزون وضريبة وأصول وقيد نتائج السنة وإصدار عددي للإجراء",
       {1: "route.companyId", 4: "route.id (period only)", 7: "PeriodClosePreview.Checks.Code (GL only)", 8: "PeriodClosePreview.Checks.Passed (GL only)"}, [(9, "AuditEvent.ActorId: period.closed"), (10, "AuditEvent.ActorId"), (11, "AuditEvent.OccurredAt")])
MAP["GL11"]["test_file"] = "backend/tests/Ameen.Erp.Tests/RateAndCloseTests.cs; backend/tests/Ameen.Erp.Tests/HostedApiIntegration.cs; backend/tests/Ameen.Erp.Tests/SqlIntegration.cs"
screen("GL12", "POST /companies/{companyId}/periods/{id}/reopen",
       "فتح فترة بصلاحية مستقلة وسبب مسجل", "مرجع موافقة مستقل ونافذة فتح مؤقتة وإعادة إقفال تلقائي",
       {1: "route.companyId", 4: "route.id", 5: "ChangePeriodRequest.Reason", 10: "Version"}, [(8, "AuditEvent.ActorId"), (9, "AuditEvent.OccurredAt")])
screen("COST01", "GET/POST /companies/{companyId}/cost-centers; GET /search; GET/PUT /{id}; POST /{id}/status; GET /{id}/usage|audit-events",
       "شجرة مراكز بلا دورات وبنطاق فرع متسق؛ أسماء وملاحظات ومسؤول فعال وإصدار عددي؛ تعديل وتعطيل وبحث واستخدام وتدقيق",
       "قائمة نوع المركز غير محددة في التحليل وتنتظر توضيح المستخدم؛ الموازنات والتوزيعات وتقارير تجميع الشجرة لم تنفذ",
       {1: "CostCenter.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "ParentId", 9: "BranchId", 10: "ResponsibleMembershipId -> active company Membership with center branch scope", 14: "Revision"},
       [(12, "AuditEvent.ActorId: cost-center.created"), (13, "AuditEvent.OccurredAt: cost-center.created"), (15, "AuditEvent.OccurredAt: cost-center.edited / cost-center.status-changed")])
MAP["COST01"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in
    ["CostCenterTests.cs", "FoundationSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("INV03", "GET/POST /companies/{companyId}/units; GET/PUT /{id}; POST /{id}/status; GET /{id}/audit-events",
       "دليل وحدات بأسماء ورمز قياسي ووصف ودقة كمية وإصدار عددي؛ بحث وتعديل وحالة وتدقيق؛ لا معامل تحويل عام",
       "عرض الاستخدام وقفل البنية عند ربط ITEM_UNIT ينفذان مع INV04؛ لا باركود أو تحويل أو حركة مخزون بعد؛ الرمز القياسي تحقق شكلي بلا سجل خارجي؛ دقة كمية 0..6 حد تقني معلن",
       {1: "UnitOfMeasure.Id", 2: "CompanyId", 3: "Code", 4: "Name", 5: "EnglishName", 6: "IsActive", 7: "Notes", 8: "StandardCode", 9: "QuantityScale", 10: "Description", 13: "Revision"},
       [(11, "AuditEvent.ActorId: unit.created"), (12, "AuditEvent.OccurredAt: unit.created"), (14, "AuditEvent.OccurredAt: unit.edited / unit.status-changed")])
MAP["INV03"]["test_file"] = "; ".join("backend/tests/Ameen.Erp.Tests/" + name for name in ["UnitTests.cs", "UnitSqlIntegration.cs", "SchemaUpgradeIntegration.cs", "HostedApiIntegration.cs"])
screen("RPT01", "GET /companies/{companyId}/reports/trial-balance",
       "أول المدة والحركة وآخرها بالوظيفية؛ تجميع مستوى الدليل ومرشح المركز وإخفاء الصفري دون ازدواج", "طرق عرض العملات ومستوى التفصيل والطباعة والتصدير وتحسين أحجام كبيرة",
       {1: "CompanyId", 2: "BranchIds", 3: "query.from", 4: "query.to", 9: "query.accountLevel (null = posting leaves; 1..20 = maximum displayed depth)", 10: "query.costCenterId", 11: "query.showZero"})
screen("RPT02", "GET /companies/{companyId}/reports/accounts/{accountId}/ledger",
       "حساب واحد برصيد افتتاحي وجارٍ صحيح عبر الصفحات؛ مرشح مركز ومرجع خارجي يطبقان على الافتتاح والحركة", "نطاق حسابات وتصدير وطباعة وطرق عرض إضافية",
       {1: "CompanyId", 2: "BranchIds", 3: "query.from", 4: "query.to", 11: "query.costCenterId", 12: "query.externalReference (exact case-sensitive match)"})

# Partial/derived semantics are explicit, never counted as full field implementation.
for code, n, binding, status in [
    ("SYS03", 10, "FiscalPeriod.IsClosed: general ledger only", "partial"),
    ("SYS05", 5, "Company.FunctionalCurrency", "derived"),
    ("SYS06", 6, "Membership.IsActive (ERP membership, not identity account)", "partial"),
    ("SYS06", 12, "Membership.IsActive (ERP membership only)", "partial"),
    ("SYS09", 8, "Company.RequireSeparateApprover always true; no configuration API", "partial"),
    ("SYS10", 5, "scope JV:FiscalYear.Id", "derived"),
    ("SYS10", 6, "fixed JV-{yearCode}-", "partial"),
    ("SYS10", 7, "NumberSequence.LastNumber + 1; internal only", "partial"),
    ("GL03", 4, "FiscalPeriod.FiscalYearId", "derived"),
    ("GL03", 8, "currency is on each line; no single document currency", "partial"),
    ("GL03", 9, "rate is on each line; no single document rate", "partial"),
    ("GL03", 13, "Source: Manual / Recurring / Reversal provenance; configurable journal-type catalog is pending", "partial"),
    ("GL04", 8, "GenerationMode: Manual only; produces Draft with independent approval/posting", "partial"),
    ("GL07", 4, "FiscalPeriod.FiscalYearId", "derived"),
    ("GL07", 8, "Lines[].CurrencyCode copied from original", "partial"),
    ("GL07", 9, "Lines[].ExchangeRate copied from original", "partial"),
    ("GL11", 3, "FiscalPeriod.IsClosed: general ledger only", "partial"),
    ("GL11", 7, "PeriodClosePreview.Checks.Code: general ledger checks only", "partial"),
    ("GL11", 8, "PeriodClosePreview.Checks.Passed: general ledger checks only", "partial"),
    ("RPT01", 5, "Currency: functional currency only; no selection", "partial"),
    ("RPT01", 7, "Posted and Reversed originals only; no selection", "partial"),
    ("RPT02", 5, "Currency: functional currency; original line amounts retained", "partial"),
    ("RPT02", 7, "Posted and Reversed originals only", "partial"),
    ("RPT02", 9, "route.accountId: one account only", "partial"),
]:
    MAP[code]["fields"][f"{code}-F{n:02}"] = dict(status=status, binding=binding)

RULES = {
    "BR01": ("manual-journal", "Decimal واتزان دقيق؛ رفض فقدان الدقة؛ فروق التقريب تحتاج سطرًا صريحًا", "Program.cs: BR01 / Wire contract"),
    "BR02": ("partial", "شركة وفرع وحساب ومركز وعملة وسعر؛ لا مخازن أو أطراف أو ضرائب بعد", "Program.cs: BR02; SqlIntegration.cs: company isolation"),
    "BR03": ("manual-journal", "تاريخ الوثيقة والأثر ووقت الترحيل مستقلة؛ فترة مفتوحة", "Program.cs: BR03"),
    "BR04": ("partial", "حساب فعال فرعي وعملة ومركز صحيح؛ المشروع والطرف لاحقًا", "Program.cs: BR04"),
    "BR05": ("partial", "ذرية القيد والتدقيق وOutbox والنتيجة وسجل التوليد الدوري؛ الدفاتر المساعدة والعامل الخارجي لم تنفذ", "Program.cs: BR05; SqlIntegration.cs / RecurringSqlIntegration.cs: constraint rollback"),
    "BR06": ("partial", "Idempotency per company/actor/operation/body؛ قيد دوري فريد لكل قالب وفترة؛ مصادر بقية المستندات لاحقًا", "Program.cs: BR06; RecurringTests.cs; SqlIntegration.cs / RecurringSqlIntegration.cs: concurrency"),
    "BR07": ("manual-journal", "حماية المرحل وعكس مرتبط بفترة مفتوحة وتثبيت سعر الأصل", "Program.cs: BR07"),
    "BR08": ("partial", "منع قيد يدوي للحساب الرقابي؛ مطابقة دفاتر مساعدة لاحقًا", "Program.cs: BR08"),
    "BR09": ("partial", "تدقيق الأوامر وفصل المنشئ والمعتمد؛ مسارات مراجعة متعددة لاحقًا", "Program.cs: BR09 / Workflow"),
    "BR10": ("manual-journal", "سعر موجب مؤرخ ثابت ومبالغ أصلية ووظيفية وعكس تاريخي", "Program.cs: BR10"),
    "BR14": ("partial", "منع إقفال الأستاذ مع قيود معلقة؛ الإقفال الشامل والسنوي لاحقًا", "Program.cs: BR03; SqlIntegration.cs: close/post race"),
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
        "module-COST": "شجرة مراكز ومسؤول وتعديل وتعطيل؛ نوع المركز والموازنات والتوزيعات لم تكتمل",
        "module-RPT": "ميزان وأستاذ جزئيان",
        "system-map": "أساس SYS وGL وبعض COST وRPT؛ بقية الأقسام لم تبدأ",
        "data-model": "شركات وفروع وحسابات وفترات وقيود وسطـور وتدقيق وOutbox؛ امتداد قوالب دورية بإصدار وتوليد؛ DOCUMENT والدفاتر المساعدة لاحقًا",
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
                raise SystemExit("Traceability stale: " + str(path.relative_to(ROOT)))
        else:
            path.write_text(content)
    print(f"PASS: analysis hashes; 150 screens; 2759 fields; 28 diagrams; 20 rules; {len(MAP)} partial screens; no full-screen completion claim.")


if __name__ == "__main__":
    main()
