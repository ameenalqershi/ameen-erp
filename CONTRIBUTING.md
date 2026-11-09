# Contributing to Ameen ERP | المساهمة في المشروع

شكرًا لرغبتك في المساهمة. نرحب بمساهمات البرمجة، الاختبار، التوثيق، مراجعة العقود، قابلية الاستخدام، وترجمة الشروحات. Contributions from any experience level are welcome.

## قبل البدء / Before you start

1. اقرأ [Start Here](docs/community/START_HERE.md)، والبنية في [Architecture](docs/community/ARCHITECTURE.md)، وحالة التنفيذ في [PROGRESS](docs/backend/PROGRESS.md).
2. تحقق من [Issues](https://github.com/ameenalqershi/ameen-erp/issues) وطلبات الدمج المفتوحة لتفادي ازدواج العمل.
3. في الأعمال الوظيفية الكبيرة، افتح Issue يحدد المتطلبات والاعتماديات وحدود الميزة قبل البرمجة. لا تحسم سياسة مالية أو أمنية غير معتمدة من نفسك.
4. ابدأ من فرع feature مستقل مبني على `main` الحالي؛ تجنب الدفع إلى فروع الوحدات التي يعمل عليها الآخرون.

## خطوات الطلب / Pull request workflow

```bash
git clone https://github.com/ameenalqershi/ameen-erp.git
cd ameen-erp
git switch -c docs/improve-onboarding
# Make one focused change, then commit.
git add .
git commit -m "docs: clarify contributor onboarding"
git push -u origin docs/improve-onboarding
```

إذا لم تكن لديك صلاحية دفع، أنشئ Fork وافتح PR من فرعك. اربط الـPR بالـIssue، واكتب: ماذا تغيّر، لماذا، اختباراتك، تأثيره على API/SQL/العزل، وأية صور أو أدلة ملائمة. لا تنشر سجلات تحمل بيانات حساسة.

## Quality gates

- **Documentation-only:** تحقق من الروابط المحلية والأمثلة والنصوص.
- **Backend:** من `backend` شغّل `dotnet restore Ameen.Erp.sln --locked-mode` و`dotnet build Ameen.Erp.sln -c Release --no-restore`، ثم برنامج الاختبارات التنفيذي المبين في [Development](docs/community/DEVELOPMENT.md).
- **Database:** أضف ترقية مرقمة قابلة للفحص ولا تعدل ترقية تاريخية مطبقة؛ وثّق مسار التطبيق/الرجوع.
- **Financial changes:** احفظ عزل الشركة والفرع والصلاحيات، الثبات المالي، الأثر الذري، التدقيق، والدقة العشرية. اختبر المسارات السلبية وإعادة المحاولة.
- **Contract changes:** حدّث OpenAPI والتوثيق ومصفوفة التتبع والاختبارات ذات الصلة فقط. لا تغيّر ملفات التحليل المرجعي دون موافقة صريحة على إعادة تحليله.
- **Integration:** تستخدم SQL Server تجريبيًا منفصلًا؛ لا تُستخدم قواعد الإنتاج، ولا تحفظ بياناتها في artifacts.

## Scope and review

أفضل طلبات الدمج محددة بمخرجات قابلة للتحقق. يمكن تقسيم الأعمال الكبيرة إلى PRs مترابطة مع توضيح تبعياتها، دون إغلاق المهمة الأم قبل اكتمال الشروط. لا يُعد نجاح CI موافقة تلقائية على القرار المالي أو الإنتاجي. قد يُطلب منك تحديث PR قبل الدمج.

## أسلوب الحوار / Community etiquette

ناقش الأفكار باحترام، واربط الادعاءات بأدلة، ووضح ما اختبرته وما لم تختبره. ابدأ بالتصحيح أو السؤال الموثق، ولا تدعِ تنفيذ سياسة أو شاشة كاملة إذا كانت جزئية. راجع [Code of Conduct](CODE_OF_CONDUCT.md).

## الانضمام إلى الفريق

المساهمة العامة مفتوحة عبر Issues/PRs، ولا تتطلب دعوة. أما عضوية فريق الصيانة أو صلاحية الكتابة فتُمنح بقرار المالك بعد مساهمات موثوقة واحتياج فعلي. التفاصيل في [GOVERNANCE.md](GOVERNANCE.md).

## English summary

Find or discuss a scoped issue, work on a fork or feature branch, validate your change, and open a PR with evidence. Preserve tenant and permission boundaries, decimal precision, auditability, and transaction safety. Do not change reference analysis or infer unapproved accounting policies. Maintainer access is earned and invite-based, not automatic.
