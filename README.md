# WOW ERP | أمين ERP

**An Arabic-first, modular accounting ERP project — open for engineering collaboration.**

نظام ERP محاسبي قيد التطوير، يستهدف احتياجات الأعمال في اليمن والخليج. يركز التنفيذ الحالي على خدمات الباك إند وقواعد المحاسبة وضبط البيانات، مع تصميم يتيح توسيع الوحدات تدريجيًا.

> **Status / الحالة:** Active development — not production-ready. The repository is publicly visible, but open-source licensing is **pending maintainer approval** in this proposal. Public visibility alone does not grant redistribution rights.

[ابدأ من هنا](docs/community/START_HERE.md) · [English quick start](#english-quick-start) · [المساهمة](CONTRIBUTING.md) · [خارطة الطريق](ROADMAP.md) · [الأمان](SECURITY.md)

## ماذا يحتوي المشروع اليوم؟

- ASP.NET Core APIs على **.NET 10** مع EF Core وSQL Server.
- تصميم **Modular Monolith** بطبقات Domain / Application / Infrastructure / API.
- نواة تعدد الشركات والفروع والعملات، حسابات الأستاذ والقيود والتقارير والتهيئة، وميزات جزئية في المخزون والإجراءات الأخرى.
- مصادقة OIDC/JWT، والتحقق من صلاحيات الشركات والفروع، وعمليات كتابة مالية ذات معاملة ذرية مع سجل تدقيق وOutbox ومنع تكرار الأوامر.
- اختبارات أعمال وتكامل SQL ومعاينة HTTPS/API، مع أدوات تتبع التنفيذ ومطابقته للمتطلبات.
- حزمة تحليل مرجعية واسعة تشمل **150 شاشة ووظيفة** موزعة على 15 قسمًا؛ **لا تعني هذه الأرقام اكتمال تنفيذها**.

لا توجد واجهة Flutter مكتملة؛ وهي اتجاه مقترح مستقبلي. لا يعد هذا المستودع حاليًا منتج ERP جاهزًا للنشر التجاري.

## خريطة المستودع

```text
backend/src/                 API / Application / Domain / Infrastructure
backend/tests/               Executable .NET tests and SQL integration
backend/database/            Versioned SQL migrations and runtime role
backend/scripts/             Verification and traceability tools
backend/openapi/v1.json      API contract snapshot
docs/backend/                Architecture, status, contracts and implementation detail
docs/community/              Start here, onboarding and contributor guidance
muhaseb-erp-analysis/        Reference analysis, diagrams and screen inventories
.github/                     CI, issue forms and contribution workflow
```

## ابدأ التشغيل والتحقق

**المتطلبات:** .NET 10 SDK، اتصال NuGet؛ تحتاج اختبارات SQL أو تشغيل API إلى SQL Server 2022+ وخادم هوية OIDC مناسب.

```bash
cd backend
dotnet restore Ameen.Erp.sln --locked-mode
dotnet build Ameen.Erp.sln -c Release --no-restore
dotnet tests/Ameen.Erp.Tests/bin/Release/net10.0/Ameen.Erp.Tests.dll
```

**مهم:** اختبارات المشروع حزمة تنفيذية؛ لا تعتمد على `dotnet test` وحده. لا تشغّل اختبارات SQL على إنتاج. راجع [الدليل التفصيلي](backend/README.md)، فهو يشرح `AMEEN_TEST_SQL_CONNECTION` وتكوين الشهادات والهوية. بعض أرقام إصدارات الترقية المكتوبة في الوثائق القديمة تحتاج تحديثًا وفق الملفات الفعلية قبل النشر.

## مسارات المعرفة

| المجال | المرجع |
| --- | --- |
| نقطة البداية للمساهم | [START_HERE](docs/community/START_HERE.md) |
| البنية والمسؤوليات | [Architecture overview](docs/community/ARCHITECTURE.md) |
| إعداد التطوير المحلي | [Development workflow](docs/community/DEVELOPMENT.md) |
| العقود الحالية | [Backend API](docs/backend/api.md) |
| الخطة والقيود الفعلية | [Backend progress](docs/backend/PROGRESS.md) |
| مصفوفة المطابقة | [Traceability](docs/backend/traceability.md) |
| التحليل الأصلي | [Analysis index](muhaseb-erp-analysis/README.md) |
| فحص جاهزية الإنتاج | [Validation and limits](docs/backend/validation.md) |

## انضم كمساهم

**مرحب بالمطورين والمراجعين وكتّاب التوثيق والمختبرين.** يمكنك البدء بتصحيح وثيقة، أو اختبار مستقل، أو تحسين مأخوذ من مشكلة موثقة. العضوية الرسمية والصلاحيات الإدارية ليست شرطًا لإرسال Pull Request.

1. اقرأ [دليل البداية](docs/community/START_HERE.md) و[قواعد المساهمة](CONTRIBUTING.md).
2. راجع [Issues](https://github.com/ameenalqershi/wow-erp/issues) وابحث عن مهمة محددة ذات قبول واضح، أو ناقش مقترحك عبر Issue.
3. اعمل في فرع مستقل، ثم أرسل Pull Request صغيرًا بقدر يكفي للمراجعة مع اختبارات وأدلة.
4. للمشكلات الأمنية، لا تنشر تفاصيل الاستغلال؛ اتبع [SECURITY.md](SECURITY.md).

**Policy:** No unreviewed merges or access grants. Sustained contributions may lead to maintainer invitations based on trust, code quality, and project needs; see [Governance](GOVERNANCE.md).

## الترخيص

ملف `LICENSE` المقترح في التغييرات التحضيرية يحتاج مراجعة مالك حقوق الشيفرة ومصدر المواد المرجعية والتبعيات قبل اعتماد الترخيص. لا تعتبر مجرد إتاحة المستودع عامًا إذنًا قانونيًا بإعادة الاستخدام قبل دمج رخصة صالحة.

## English quick start

WOW ERP is an **in-progress** Arabic-first accounting ERP backend built with .NET 10, ASP.NET Core, EF Core, and SQL Server. It is organized as a modular monolith with tenant-aware financial workflows. The analysis documents cover a broader scope than what is implemented. There is no production-readiness claim and no completed Flutter client. Start with [contributor onboarding](docs/community/START_HERE.md), follow [development setup](docs/community/DEVELOPMENT.md), and read [contribution guidelines](CONTRIBUTING.md) before opening a PR.
