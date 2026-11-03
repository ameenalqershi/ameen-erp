# سجل تقدم وتسليم الباك إند

## SYS01-OP02 — company save and status atomicity acceptance (3 November 2026)

Already implemented company PUT and status endpoints now have additional focused positive/negative acceptance. Domain tests deny status changes by actors without Configure permission, assert stale version rejection, single idempotent replay and exactly one audit/revision per accepted status, with successful reactivation. Disposable SQL integration forces a company.status-changed audit constraint failure and asserts transaction rollback of status, GUID version, numeric revision, audit and receipt; the exact same request then succeeds after removing the temporary disposable-test constraint. Production HTTPS/JWT/SQL already tests company save/edit, inactive-company write denial and reactivation. No original analysis, migrations or policy changes. Final acceptance requires green CI on the exact sys head; SYS01 overall remains partial.



## SYS01-OP03 — company usage and audit read acceptance (3 November 2026)

Existing GET /api/v1/companies/{companyId}/usage and /profile-audit-events are tested without changing the API contract: an authenticated all-branch owner receives an authoritative SQL-derived company-scoped count for branches, accounts, currencies, fiscal years, journals, recurring templates and exchange rates, including the functional-currency-change guard. Audits return only company-profile actions, are pageable, and remain readable with an inactive company. The production HTTPS/JWT/SQL host checks an initial new tenant with exactly one bootstrap currency, no other usage, an inactive state and an existing three-event profile audit; an unrelated JWT must receive HTTP 403 for both resources. SQL integration independently compares the usage API with the actual database tables and checks audit pagination and non-member denial. No original specification, schema, financial policy or production endpoint changes. Completion requires passing CI on the exact sys commit; SYS01 overall remains partial due approved-country/FX-conversion gaps.

## SYS01-FG04 — tax registration, fiscal start and locality acceptance (3 November 2026)

SYS01-F13/F14/F19/F20: extended isolated SQL and production HTTPS/JWT GET-after-PUT verification for TaxRegistration with preserved leading zeros, required FiscalYearStartsOn, required City and optional District. Existing domain acceptance already rejects blank City and fiscal-year edits that would contradict an existing calendar; no new policy was introduced for conditionally required tax registration. Source specification and diagrams are unchanged. CI on the exact `sys` SHA is required before final verification.

## SYS01-FG05 — address fields and leading-zero acceptance (3 November 2026)

SYS01-F21..F24: extended the existing company positive SQL and production HTTPS/JWT acceptance to roundtrip mandatory Street and optional BuildingNumber, PostalCode and AdditionalNumber, preserving leading zeros in all three text identifiers. Added a negative domain test rejecting an empty required Street alongside the existing length bound. Existing authorized PUT, row revision, audit and replay checks remain. No new validation policy or schema change; source dictionary/diagrams unchanged. CI on the exact `sys` SHA is required for final task acceptance.

## SYS01-AC01 — positive legal-entity and independent-book SQL acceptance (3 November 2026)

Added a focused real-SQL acceptance path for two independent legal companies: separate functional-currency bootstrap and membership, same account code with independent account identity, posted journal totals present only in the original company, an empty secondary company trial balance, and a denied cross-tenant report read by an otherwise authorized user of the primary company. This is an explicit positive verification of SYS01 company/book isolation; it does not assume an additional hidden ledger table or complete the remaining SYS01 screen fields. Source analysis and diagrams are unchanged. CI on the exact `sys` commit remains necessary before final task acceptance.

## SYS01-FG06 — company audit-field acceptance (3 November 2026)

Added focused SQL-backed assertions for `SYS01-F15..F18`: `company.created` and `company.edited` preserve the actual caller (`ActorId`), valid ordered `OccurredAt` timestamps and correct company/resource scoping, while the company revision advances exactly once despite concurrent idempotent replay. The production HTTPS/JWT acceptance now verifies all three profile audit events (`created`, `edited`, `status-changed`), their originating subject, tenant/resource identifiers and chronological timestamps, and a non-member JWT's HTTP 403 audit denial. The original dictionary maps creator, creation and last modification to authoritative audit events rather than mutable Company profile fields. All changes are acceptance tests only, with no new schema, policy, endpoints or modifications to the original analysis. Final acceptance requires isolated SQL and production HTTPS/JWT/OpenAPI/Release CI on the exact `sys` commit SHA; no claim that the complete SYS01 screen is implemented.

## SYS01-FG01 — identity/code/Arabic name coverage and ID generation review (3 November 2026)

The atomic Notion task SYS01-FG01 covers company ID, company scope, code and Arabic name. Existing company creation accepts a GUID supplied in CreateCompanyRequest.Id by the platform provisioning caller, whereas the original SYS01-F01 field labels the internal ID "computed/system." Do not silently equate a caller-supplied GUID with a server-generated field or change the provisioning/API contract without resolving that interpretation. Focused acceptance checks the current persisted ID, code and Arabic name in isolated SQL and production HTTPS/JWT/SQL, with commits 6ecc3a888167bfae98995e0771ca1e0db4ceafe7 and e030702f5e7de85f1e49ce5376a30330d0eabf2e. These additions are **not** evidence of complete source conformance for SYS01-FG01; keep the Notion task in progress and verify CI. No original analysis or migration change.

## SYS01-FG02 — verified-field acceptance under CI (3 November 2026)

Notion task SYS01-FG02 covers EnglishName (optional), IsActive (required), Notes (optional) and LegalName (required); all are mapped as implemented in docs/backend/fields-implementation.csv and persisted by Company.Configure. Focused domain tests now assert their roundtrip, CompanySqlIntegration checks notes persisted in the isolated SQL-backed service, and HostedApiIntegration checks EnglishName, Notes and LegalName through production HTTPS/JWT/SQL GET following authorized PUT. The existing host workflow verifies disabled/enabled company status, revision and audit. Related commits baa08a7691effaf16d461c60fc05e55034f1e308, 6444bf9610d5369d68d094afc2aba4d29591f1b9 and bf6430715b86e1c8863f67f0ff46bc784ebf442d require successful final CI before this atomic field task is closed. No schema/policy/analysis changes; SYS01 screen remains partial due unrelated policy gaps.

## SYS01-AC02 — authenticated negative HTTP acceptance under CI (3 November 2026)

Beyond the 376/376 green domain and isolated SQL evidence in [CI #41561704708](https://github.com/ameenalqershi/ameen-erp/actions/runs/41561704708), production host acceptance at 6dea75f75117f74642e2322dab9290f606379c59 requests a real HTTPS PUT signed with authorized JWT to switch an existing journal-bearing company to a valid NOK card. Expected HTTP 409 functional_currency_in_use must leave SQL-backed company currency, scale, revision, GUID token and audit count unchanged. This must pass final CI before Notion SYS01-AC02 is marked complete; it is not an implementation of a historical currency-conversion project.

## SYS01-AC02 / SYS06-AC02 — focused SYS acceptance in progress (3 November 2026)

- **SYS06-AC02 (partial)**: Program.cs asserts disabled ERP membership remains persisted with a changed GUID concurrency token, a new audit record, and denied journal replay and account reads. The initial assertion used numeric version arithmetic and failed compilation; fixed on 569cae5ed48b54d6ecb541dad14bcdf4e2eabc95, which passed [branch CI #41561059527](https://github.com/ameenalqershi/ameen-erp/actions/runs/41561059527), **376/376 tests, 0 failed**, including SQL/HTTPS/JWT/OpenAPI/Release. This does **not** implement or close identity-provider login uniqueness, password-export exclusion, MFA or SYS06 as a whole. Notion SYS06-AC02 remains in progress.
- **SYS01-AC02 (under verification)**: Added an application negative acceptance after a cancelled journal: refusing functional-currency change must not update currency, scale, revision, GUID version or audit. Added a separate isolated live SQL test in CompanySqlIntegration: after an existing persisted journal, a proposed change from the functional currency to an active NOK card must return functional_currency_in_use without changing company version, revision, SQL audit count or idempotency receipt. These tests do not authorize a historical currency-conversion project or modify original analysis. Latest SYS01 test commits 47f1f66d01cba4c5ce73788e12773526414b9b28, 298f045df472f59aae011c9eacf050ec9524caf0, and compile-helper fix ac4c758f8fbd487e68344070da5b40a72d16eb45 require CI approval on the resulting head before task completion; Notion SYS01-AC02 remains in progress.
- Both batches are scoped to SYS. The GL agent's files, source diagrams and financial policies are unchanged.

## SYS04-CTRL01 — verified currency precision and functional currency guard (3 November 2026)

The source control `SYS04-CTRL01` forbids retroactive precision changes and locates functional currency configuration in SYS01 company. Current `CurrencyService` enforces forbidden functional-currency disable and scale edits, and rejects precision changes once cards are referenced by accounts, FX rates, journals or recurring templates. Domain and service tests cover these references, active status, audit, rollback, idempotency and permission; the added production HTTPS/JWT/SQL negative acceptance `SYS04-AC02` demonstrates an in-use CAD account prevents a scale 2→3 edit without changing revision, version or audit. This rule was already implemented and is now accepted under the same successful **375/375 branch and PR CI** linked in the SYS04-AC02 evidence above. No new financial policy or original-analysis edits; broader SYS04 migration decisions remain open.


## SYS04-AC02 — in-use precision rejection over authenticated SQL host (3 November 2026)

The original SYS04 negative control states that currency precision cannot be changed retroactively and functional currency is configured at company level. Existing `CurrencyTests` reject precision changes for functional currencies, account references, historical FX rates, journal entries and recurring templates; prior accepted edits remain legal before use. New production-host HTTPS/JWT/SQL regression `SYS04-AC02` creates a CAD currency and account reference, issues an authorized PUT attempting scale 2→3, expects HTTP 409 `currency_precision_in_use`, then verifies the SQL-backed scale, revision, version and audit-event total remain unchanged. No new financial rule, migration, entity or original source analysis. **SYS04-AC02 accepted on sys:** [branch CI #41558932297](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558932297) and [PR CI #41558995244](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558995244) passed for commit `c9cc84059efbe5d6c21da7519c10953c57d916c9` (with the test first added in `d7645fc0a984ba7885601eafc4f670cf030a4ed5`): **375/375 tests passing, zero failures**, isolated SQL, HTTPS/JWT, OpenAPI, Release. Log confirms `HOST PASS SYS04-AC02 referenced currency precision edit refused atomically over HTTPS/JWT/SQL`. The same proven functional/used-currency protections satisfy atomic screen control `SYS04-CTRL01`; this does not complete SYS04's ISO reference or historical migration gaps.


## SYS05-GAP03 — quote is advisory, never reserves an exchange rate (3 November 2026)

Existing exchange-rate service logic selects the latest dated effective quote; journal creation rechecks that the explicitly supplied rate is still the latest effective rate on its accounting date. Focused acceptance on `sys` adds an application test and a production-host HTTPS/JWT/SQL test. Three repeated quotes must leave audit, idempotency, journal and numbering counters unchanged; creating a newer effective rate supersedes an earlier quote; trying to create a journal with the earlier rate must fail `stale_exchange_rate` with no persisted journal, audit, sequence or receipt; a corrected journal using the new rate succeeds with an immutable exchange-rate snapshot on its lines. This does not create financial policies, modify migrations, alter original analysis or touch production data.

**Verified focused acceptance:** commit `bf92d7ce1441543aafcf80255a9a1125e665140a` passed [CI #41558785405](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558785405), and its documentation commit `eb05e3afcf113b91f548cb9f6e8ab3dc7ef57176` passed [CI #41558844829](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558844829): **375/375 passed, 0 failed**, including isolated SQL, signed HTTPS/JWT/OpenAPI and Release. The SYS05-GAP03 acceptance test and Notion task are complete on `sys` only. Subsequent work on `sys` must pass CI independently before integration. SYS05 itself remains partial: approved price types/branch scope and historical revaluation are separate gaps. [GitHub sys](https://github.com/ameenalqershi/ameen-erp/tree/sys) and [PR #30](https://github.com/ameenalqershi/ameen-erp/pull/30) remain draft, not merged.


## SYS04-AC01 — currency positive acceptance review (3 November 2026)

The supported SYS04 positive condition is already verified end to end: currency cards persist ISO-like currency code, Arabic/English labels, fractional units, display symbol, scale and server revision; GET/detail and search return the card, and created/edited/status audit is retrievable. `CurrencyTests` exercise labels/symbol/fraction, revision and active search with identity/replay; the production `HostedApiIntegration` creates and edits GBP through authenticated HTTPS, then reads filtered SQL-backed search and audit. [CI #41558505651](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558505651) and [PR CI #41558514949](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558514949) passed on `sys=447da3fc9a38498af8e8112ad6179600f7bfe716`, with isolated SQL/JWT/OpenAPI/Release and full project tests. This closes only the atomic positive acceptance task `SYS04-AC01` by reconciling already-implemented behavior with Notion; country ISO reference and historical precision migration remain unresolved and excluded.


## SYS04-OP03 — currency usage/audit HTTP acceptance (3 November 2026)

Added a focused production-host HTTPS/JWT/SQL assertion to the existing currency workflow: a disabled currency still yields an authoritative tenant-scoped usage snapshot with `currencyId`, `isFunctionalCurrency` and reference counts; unrelated JWT subjects receive HTTP 403 for usage and audit access. The existing production test already checks three creation/edit/status audit events, while `CurrencyTests` covers historical usage, global scope, functional currency references, and unauthorized audit. This is evidence for Notion `SYS04-OP03` only, with no currency entity/API contract/migration/analysis changes. **Accepted on sys:** SHA `64e4c9a8a152520349f796fb902c68261e882cd6` passed [branch CI #41558620184](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558620184) and [PR CI #41558626216](https://github.com/ameenalqershi/ameen-erp/actions/runs/41558626216), **374/374 passing and 0 failing**, covering SQL Server isolated, HTTPS/JWT, OpenAPI and Release. CI log confirms `HOST PASS SYS04-OP03 currency usage, audit, disabled history and forbidden access over HTTPS/JWT/SQL`. This closes only the SYS04-OP03 atomic task on `sys`; SYS04 overall remains partial.


## SYS04-OP02 — currency save/edit/deactivation acceptance evidence review (3 November 2026)

Existing authorized `CurrencyService.EditCurrency` / `SetCurrencyStatus` preserve optimistic version checks, idempotency, atomic audit and mandatory reasons. Scale edits are denied for a functional currency and for currencies already used by account cards, historical rates, journals, and recurring templates; a functional currency cannot be disabled. `CurrencyTests` cover successful edit/replay/version, disabled state, functional/used precision protection, in-use reports, permission denial and transaction rollback. `HostedApiIntegration` exercises currency edit via PUT, status via POST, audited status, and filtered search on production HTTPS/JWT/SQL. [Branch CI #41557936570](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557936570) and [PR CI #41557943713](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557943713) passed at `fb7561a263d5c7698d6b285c8e0aed8de6c8bbf9`: **374/374 tests, 0 failed**, isolated SQL, HTTPS/JWT, OpenAPI and Release. `SYS04-OP02` was already correctly implemented; only its Notion acceptance status and evidence were stale. No new currency policy, schema, production data or original analysis changed. SYS04 broader ISO/historical-migration gaps remain.


## SYS04-OP01 — currency search/create acceptance evidence review (3 November 2026)

Existing implementation (no replacement required): `CurrencyService.CreateCurrency` enforces tenant-scoped duplicate codes and authorized Configure permission, uses idempotent audited command receipts, and validates code/scale/labels. `CurrencyService.SearchCurrencies` provides authorized Read, active/inactive and name/ISO search with deterministic pagination. Positive and negative cases exist in `CurrencyTests` (create/read/search/duplicate/invalid scale/permission) and in the production `HostedApiIntegration` test (CreateCurrencyRequest for GBP, search by English name and active state, audit). The full shared backend verification on branch `sys` at `fb7561a263d5c7698d6b285c8e0aed8de6c8bbf9` passed [branch CI #41557936570](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557936570) and [PR CI #41557943713](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557943713), **374/374 tests, 0 failures**, with isolated SQL, production HTTPS/JWT, OpenAPI and Release. The atomic Notion task `SYS04-OP01` is verified as already implemented on `sys`; no new currency code was required. SYS04 overall stays **partial** pending approved ISO reference, historic metadata cleanup and historical precision migration policies; not implemented or claimed here.


## SYS02-GAP03 — legacy branch address readiness SQL acceptance (3 November 2026)

Added focused live SQL acceptance to `BranchSqlIntegration` on branch `sys` for a pre-existing branch whose city/address columns were null after upgrade: the public journal-create API rejects `branch_profile_incomplete` before validating lines or reserving a number, and the public branch status transition rejects the same error even when the stored branch was historically marked active. Both use the existing internal readiness guard without calling an inaccessible method from the test assembly. The rejection must preserve row version, state, audit history, and command receipt; completing both location fields through the authorized edit command restores operational readiness with exactly one audited update. Covers the existing SYS02 prohibition on operating legacy branches before profile completion, without altering original analysis or production data. **Accepted for SYS02-GAP03 on sys:** commit `fb7561a263d5c7698d6b285c8e0aed8de6c8bbf9` passed [branch CI #41557936570](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557936570) and [PR CI #41557943713](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557943713): **374/374 tests passing, 0 failures**, including live isolated SQL, production HTTPS/JWT, OpenAPI, Release publish and backend progress check. SQL log confirms `SYS02-GAP03 legacy incomplete branch refuses journal/activation, remains unchanged, recovers with one audit`. This closes only the focused SYS02-GAP03 acceptance on `sys`; PR #30 is not merged and other SYS02 gaps remain.


## SYS02-GAP02 — branch default cost center never auto-populates journal lines (3 November 2026)

The SYS02 specification records `Branch.DefaultCostCenterId` as configuration only, not permission to silently populate `JournalLine.CostCenterId`. This branch adds a service acceptance test and a production-host HTTPS/JWT/SQL test. Both cover the persisted branch default reference, a valid manual journal retaining null line cost centers, a `RequiresCostCenter` account that still rejects a missing dimension despite the branch default (`cost_center_required`), and the explicit cost center being accepted and persisted when the client provides it. The service test also checks that a rejected draft leaves journal, audit and idempotency receipt counts unchanged. No default injection, financial mapping policy, schema change or source-analysis edit was introduced.

**Accepted SYS02-GAP02 evidence:** commit `f57b8e61d37e57a393226bb3f1daea3d66b9eada` passed [branch CI #41557438027](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557438027) and [PR CI #41557445095](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557445095), 374/374 tests and zero failures. This includes isolated SQL Server, domain, production HTTPS/JWT/OpenAPI and Release; [Notion SYS02-GAP02](https://app.notion.com/p/4053830ba34d81a0bc6bf6ddb753df19) is complete for this precise acceptance only. [Branch sys](https://github.com/ameenalqershi/ameen-erp/tree/sys); [PR #30](https://github.com/ameenalqershi/ameen-erp/pull/30) remains separate from `main`. SYS02-GAP01 (site type and manager policy) is independent and still unresolved.



## SYS03-GAP03 — ambiguous recurring due dates in custom fiscal periods (3 November 2026)

The SYS03 custom-period rule delegates to the GL04 recurring due-date guard: a half-year containing more than one monthly due date must reject the entire generation attempt as `ambiguous_recurring_period` rather than selecting an arbitrary date. On the dedicated `sys` branch, added an actual application-service negative acceptance test using SYS03's explicit 2027 H1/H2 calendar. It verifies failure without changes to journal, generation, numbering allocation/counter, audit or idempotency receipt counts. Added a production HTTPS/JWT/SQL acceptance test creating an independent 2035 H1/H2 calendar and monthly template, requiring HTTP 422 and the named error, then verifying the journal list, generation history and audit log remain identical. No finance policy, chart, original analysis, or production data change.

**Accepted SYS03-GAP03 evidence:** commit `f57b8e61d37e57a393226bb3f1daea3d66b9eada` passed [branch CI #41557438027](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557438027) and [PR CI #41557445095](https://github.com/ameenalqershi/ameen-erp/actions/runs/41557445095), 374/374 tests and zero failures, including production HTTPS/JWT, isolated SQL Server, OpenAPI, Release and one-due positive acceptance. [Notion SYS03-GAP03](https://app.notion.com/p/4053830ba34d81b79a0cc6dc9086d456) is complete for this specific gap. Other SYS03 gaps and overall screen remain partial.

## SYS03-GAP04 — fiscal closure bypass negative acceptance (3 November 2026)

Branch `sys` adds a production-host HTTPS/JWT/SQL regression for the SYS03 invariant: a fiscal year and its periods must not be deleted or disabled as a shortcut around GL12 period-close authorization. After a successful calendar create/read, the test asserts DELETE on the fiscal-year resource is HTTP 405, POST on an undocumented status route is HTTP 404, and both the persisted calendar/periods and its audit event history remain byte-identical to before the refused calls. The generated production OpenAPI is also checked to ensure no DELETE or status route is advertised. Existing GL12 positive close/reopen tests remain the authorized lifecycle path. No financial policy, production data, or original source analysis is changed.

**Verified for this focused SYS03-GAP04 acceptance:** `sys=3462d8749ef3d200512c7448a133966753a41ee3` passed [branch CI #41555259208](https://github.com/ameenalqershi/ameen-erp/actions/runs/41555259208) and [independent CI rerun #41555475683](https://github.com/ameenalqershi/ameen-erp/actions/runs/41555475683), including isolated SQL Server, domain/application, HTTPS/JWT, OpenAPI and Release; both completed successfully. The no-delete/no-disable contract is now verified on the `sys` branch; [PR #30](https://github.com/ameenalqershi/ameen-erp/pull/30) remains open against `main`, so this is not a `main` delivery. SYS03 as a whole remains partial: calendar edits/branch policy and any other listed gaps are separate. Never infer that their acceptance is complete.

## GL03-GP05-QA — اختبار SQL العكس المجدول عند تزامن العمال (3 نوفمبر 2026)

على فرع `gl` المستقل عن عمل SYS، عولج افتراض توقيت غير مضمون في اختبار SQL: قد يصل عامل بعد تثبيت حجز العكس فلا يجد قيدًا مستحقًا، وهذا لا يعد فشلًا أو إعادة إنتاج لعملية. يتطلب الاختبار الآن أن تنجح كل محاولة *اختارت* القيد بلا أخطاء، وأن تشترك كل الاستجابات الإيجابية في معرف العكس نفسه، وأن يوجد **قيد عكسي مرتبط واحد بالضبط في SQL**. تبقى اختبارات تاريخ العكس، الحجز، وعدم إعادة التوليد بعد الإلغاء كما هي؛ لم تتغير قواعد الترحيل ولا الخدمة أو التحليل الأصلي. تحقق محلي .NET 10 من المجال/HTTP/OpenAPI: **371/371 PASS**؛ يلزم قبول SQL المعزول في GitHub CI على SHA الفرع قبل إغلاق حزمة QA. تعثّر PR #13 سابقًا مرة واحدة في شرط السباق القديم ثم نجح عند الإعادة؛ لا يعتبر هذا إثباتًا كافيًا لتجاهل الخلل في الاختبار.


## AT02 / GL03 — رفض التحايل بتاريخ الوثيقة على فترة أثر مقفلة (3 نوفمبر 2026)

أضيف اختبار قبول مستقل مطابق لـ`muhaseb-erp-analysis/docs/07-workflows-and-acceptance.md` (AT02): إذا أُقفلت فترة تاريخ الأثر، فلا يسمح اختيار تاريخ وثيقة في فترة مفتوحة بإنشاء القيد أو تعديل رأس مسودة للوصول إلى التاريخ المقفل، ويُعاد خطأ `period_closed` مع **رمز الفترة المحددة** دون إنشاء قيد أو تغيير الإصدار أو التدقيق أو سجل الطلب أو أثر الأستاذ. بالمقابل يُسمح بتوثيق تاريخ وثيقة قديم إذا كان تاريخ الأثر في فترة مفتوحة، ويظل ترحيل القيد الصحيح ممكنًا. اختبارات .NET 10 المحلية بعد الحزمة: **372/372 PASS**؛ فحص CI السابق على `gl` نجح أيضًا. أضيف تحقق SQL مستقل `JournalClosedPeriodSqlIntegration` يعمل فقط داخل قاعدة SQL مؤقتة ثم يعيد حالة الفترة السابقة في `finally`: يتحقق من رفض الإنشاء والتعديل ومحافظة القيد والتدقيق وIdempotency وOutbox وتسلسل الأرقام، ويثبت نجاح الترحيل عند فتح فترة الأثر. بناء .NET 10: **0 أخطاء و0 تحذيرات**؛ يلزم نجاح CI SQL/HTTP/JWT/OpenAPI على الرأس الجديد قبل إعلان اكتمال سيناريو AT02. لا تغيير في مخطط SQL أو قواعد الترحيل أو التحليل الأصلي.

## AT01 / GL03+GL06 — منع ترحيل قيد معتمد غير متزن (3 نوفمبر 2026)

أُضيف اختبار SQL مستقل لـAT01 من مصفوفة القبول الأصلية على فرع `gl`: يبدأ بقيد متزن معتمد، ثم يحاكي خلل بيانات محفوظة بجعل الطرفين 100/99 داخل **قاعدة اختبار مؤقتة فقط**، ويتحقق من فشل طلب الترحيل بخطأ `journal_unbalanced` مع بقاء القيد **Approved** دون أثر على التدقيق أو Outbox أو سجل الطلب أو ترقيم المستندات. يعيد الاختبار البيانات داخل `finally`، ويعيد المحاولة **بالمفتاح نفسه** ليثبت نجاح ترحيل واحد وOutbox واحد دون ردود متعارضة. اختبارات الوحدة السابقة تغطي رفض السطر غير المتزن قبل الاعتماد؛ هذه الحزمة تكمل حالة تجاوز بيانات معتمدة غير متزنة. بناء .NET 10: **0 أخطاء، 0 تحذيرات**؛ يلزم نجاح CI على الرأس الأخير بقاعدة SQL معزولة لإثبات هذه الإضافة. لا تغيير في منطق الخدمات أو مخطط SQL أو التحليل الأصلي.

## GL06-FG04 — عرض منشئ الدفعة في عقد سجل التنفيذ (3 نوفمبر 2026)

مطابقة حقول GL06-F10–F13 الأصلية أثبتت أن `JournalProcessingBatch.ActorId` يُحفظ من هوية المنفذ الأصلية لكنه لم يكن يظهر في نتيجة API `DurableBatch` رغم ظهور `CreatedAt` و`Revision` و`LastModifiedAt`. أُضيف حقل `CreatedBy` للنتيجة يقرأ **هوية المنفذ المسجلة من الخادم فقط** ويعرضها للمستخدم صاحب الدفعة بعد فحوص الصلاحيات الحالية، مع اختبارات مجال وHTTPS/JWT/SQL. لا تسمح الإضافة بتعيين منشئ الدفعة من العميل ولا تفتح سجل دفعات المستخدمين الآخرين، ولا تغير قاعدة SQL أو سياسة الترحيل أو التحليل الأصلي. يلزم نجاح CI على الرأس الأخير قبل إغلاق GL06-FG04.

## GL06-FG02 — نطاق الأرقام وتاريخ التنفيذ الخادمي (3 نوفمبر 2026)

أكد فحص القاموس الأصلي GL06-F05/F06 أن البحث عن نطاق أرقام القيود يدعم النص `JV-{FiscalYear}-...` أو مجالًا شاملًا لنفس سلسلة الترقيم والسنة والفرع المصرّح له، ويمنع نطاقًا معكوسًا أو خارج السنة أو غير صالح، دون الكتابة في الأستاذ عند البحث. الاختبارات القائمة `JournalProcessingTests` و`JournalProcessingSqlIntegration` و`HostedApiIntegration` تغطي النص وHTTP/JWT والفرع/الحالة وعزل السنة. استكمل اختبار SQL للتأكد من أن `PostedAt` يساوي وقت **الخادم** للقيدين المرحّلين فقط، بينما تبقى مسودة المستند المرفوض بلا تاريخ تنفيذ، حتى عند توازي تنفيذ الدفعة. لا تخزين لتاريخ إدخال من العميل ولا تغيير للسياسات أو المخطط أو مواصفات المصدر؛ يلزم نجاح CI SQL/HTTP/JWT/OpenAPI قبل اعتماد المهمة الدقيقة.

## SYS09-FG04 — audit atomicity negative/positive regression (AGENT-03, 2 November 2026)

This isolated SYS09-FG04 verification adds a domain regression that checks stale revision, invalid amount/currency, regressed audit timestamp, status update and stage replacement failures leave header, stages, audit values, and version unchanged; a valid edit advances revision and timestamp. No SYS09/GL14 write authorization, financial policy, original analysis, SQL schema, or production state changes. This test deliverable is not full SYS09-FG04 acceptance: audited SQL/HTTP administration remains blocked by SYS07 role policy and approved document-kind/FX mappings. Earlier branch SHA `d553cd6d401d2cc71b1d45b417c3dad96895db9e` passed [branch CI #41537610765](https://github.com/ameenalqershi/ameen-erp/actions/runs/41537610765) and [PR CI #41537618397](https://github.com/ameenalqershi/ameen-erp/actions/runs/41537618397), including isolated SQL Server, Release, HTTP/JWT and OpenAPI. The integration baseline `main=d02dbb0140a4c2e5cdf619d363c2ca9121d2743f` passed [main CI #41537496717](https://github.com/ameenalqershi/ameen-erp/actions/runs/41537496717). [PR #13](https://github.com/ameenalqershi/ameen-erp/pull/13) remains unmerged. The immediately following disabled-candidate section is a historical pre-merge checkpoint for a *different* SYS09-FG04 increment; its 'CI/main merge pending' statement does not describe current PR #13. The final documentation commit requires its own fresh branch and PR CI before acceptance. **0 new completed Notion tasks.**

## GL14-T02 — negative source acceptance, bounded partial evidence (2 November 2026)

Added one independent GL14-F06 regression for an unknown hydrated origin: source matching fails closed, reference preflight returns `InvalidSnapshot`, and restoring a valid origin still returns `UnresolvedPolicy`. No GL14 write, route activation, GL03 binding, or reversal policy was enabled. PR #17 rejected reference edit atomicity remains excluded. Local Release build and domain/model checks passed. Initial sandboxed `verify.py` ran **364/366**, with two HTTP/OpenAPI host failures due to blocked sockets; coordinator reran the exact branch on the authorized VPS host using .NET 10 and writable NuGet cache: **366/366 passed, 0 failed**, including HTTP and OpenAPI. Live SQL was not run locally; isolated SQL CI is required on the PR. GL14 remains partial; reversal separation needs an approved binding contract. Live GitHub PR overlap rechecked by the coordinator before PR creation.

## GL07-F18/F19 — تصحيح ربط حقول التدقيق المشتركة (2 نوفمبر 2026)

طُوبق قاموس الحقول الأصلي ومخطط GL وقيد اليومية المشترك: `GL07-F18` هو `JournalEntry.Revision` الصحيح، عدد صحيح يولده الخادم، و`Version` رمز UUID مستقل للتزامن؛ `GL07-F19` هو `JournalEntry.LastModifiedAt` بوقت الخادم. يوجد الحقلان في نموذج القيد، تخزين EF، وقراءة القيد/القائمة، ويغطي `JournalAuditTests` تقدم الإصدار والوقت عبر دورة العكس وفشل العمليات دون أثر. صُحح ربط التتبع والمصفوفات المولدة فقط؛ لم يتغير سلوك مالي أو تحليل أصلي أو مخطط SQL. تبقى شاشة GL07 **جزئية**: عكس المستندات التابعة والتبعيات والضرائب وتشغيل المجدول إنتاجيًا ضمن الفجوات الحالية، ولا يزيل تصحيح التدقيق هذه الموانع.

## SYS09-FG04 — disabled candidate does not reserve approval interval (3 November 2026)

On `temp/sys09-inactive-candidate-conflict-20261103` from `main=95f5ad4`, the pure SYS09 overlap preflight now returns `Clear` for an **inactive candidate** only **after** validating its own fields and the full supplied tenant snapshot. This is symmetric with existing disabled routes being excluded from conflicts. An active candidate still detects inclusive interval overlap and unresolved cross-currency policy; inactive candidates cannot conceal foreign-company or duplicate snapshot rows. Added regression coverage for disable/reactivate, overlapping amount, FX, foreign tenant and duplicate rows. **This does not authorize SYS09 persistence or GL14/GL03 posting**; EF guards remain in place. No original diagrams/fields, SQL migrations or production data changed. Local .NET 10 Release + domain/EF/HTTP/JWT/OpenAPI **363/363 passed, 0 failed**, `backend/.artifacts/sys09-inactive-candidate-20261103/verification.json`; traceability/progress checks passed. Isolated SQL CI and main merge remain pending. **0 new completed Notion tasks**.


## GL14-02 / SYS09-FG04 — reject corrupt hydrated numbering prefix and route actor (3 November 2026)

On `temp/gl14-reference-guard-integrity-20261103` based on `main=bdc96e1`, hardened the **read-only, non-authorizing** GL14 reference preflight to reject persisted SYS10 prefixes with whitespace-only or untrimmed values (in addition to control characters and `..`), and reject SYS09 route audit actors containing control characters. These malformed values are not accepted by the corresponding domain constructors and must not become valid through EF hydration. Added regression tests for corrupt and restored snapshots; valid references still return `UnresolvedPolicy`, never authorization. SYS07/SYS09/GL14 EF write guards remain active; no GL14 API or posting, original analysis, migrations, or production data changed. **Partial dependency hardening only, 0 new completed Notion tasks.** Local .NET 10 Release, domain/HTTP/JWT/OpenAPI **362/362 passed, 0 failed**; `backend/.artifacts/gl14-hydrated-integrity-20261103/verification.json`; traceability/progress checks passed. Prior [main CI #41526961794](https://github.com/ameenalqershi/ameen-erp/actions/runs/41526961794) on `bdc96e1` passed with isolated SQL. [Branch CI #41527437193](https://github.com/ameenalqershi/ameen-erp/actions/runs/41527437193) and [PR CI #41527459937](https://github.com/ameenalqershi/ameen-erp/actions/runs/41527459937) both **SUCCESS 363/363, 0 failed** on [a7b8517](https://github.com/ameenalqershi/ameen-erp/commit/a7b85170fa8495405bdcdb066b6f73d8d2f8fe73), including isolated SQL Server, Release, HTTP/JWT/OIDC, OpenAPI and tenant tests. Merged [PR #8](https://github.com/ameenalqershi/ameen-erp/pull/8) at [main bd891b7](https://github.com/ameenalqershi/ameen-erp/commit/bd891b7dfe48cabed9f675551a10620a23d163e3) (SHA `bd891b7dfe48cabed9f675551a10620a23d163e3`). Main CI [#41527904550](https://github.com/ameenalqershi/ameen-erp/actions/runs/41527904550) pending final verification. No task completion claim.

## GL14-01 — source-origin guard and stale revision regression (3 November 2026)

On `temp/gl14-optimistic-revision-20261103` from `main=fc3bfbc`, added a pure `JournalDocumentType.MatchesSource` guard: active manual/automatic types only match their declared origin, and unknown enum values or disabled types fail closed. Added regression coverage for the **existing** optimistic-revision overloads, ensuring stale edit/disable attempts cannot change GL14 fields, audit timestamps or version; valid revisions advance normally. No production authorization or GL03 source/reversal binding was activated. SYS07/SYS09 route eligibility, SYS10 series validity and GL14 EF write guard remain unresolved. This is **partial GL14-01**, not a completed Notion task. The original analysis and database schema are unchanged. Local .NET 10 Release and domain/EF/HTTP/JWT/OpenAPI verification: **360/360 passed, 0 failed** without live SQL, evidence `backend/.artifacts/gl14-optimistic-revision-20261103/verification.json`. [branch CI #41525900467](https://github.com/ameenalqershi/ameen-erp/actions/runs/41525900467) on [b21d97f](https://github.com/ameenalqershi/ameen-erp/commit/b21d97ff17ef6cbb95b9096d05aa28951c04bda9) succeeded: **361/361 passed, zero failed**, including isolated SQL Server, Release, HTTP/JWT and OpenAPI. PR CI for the documentation follow-up and main CI must still be verified before final handoff.

## SYS10-CTRL01 / GL14 dependency — ذرية تعديل الترقيم وتخصيص الرقم (2 نوفمبر 2026)

على فرع `temp/sys10-numbering-atomicity-20261102` من `main=d0a3465`، صُحّح ترتيب التحقق والتعديل داخل `DocumentNumberSeries`: تُفحص البادئة والرقم والوقت والإصدار قبل أي تغيير في `Prefix` أو `NextNumber` أو تخصيص رقم؛ ويُرفض رجوع `LastModifiedAt` إلى الماضي. أضيف اختبار انحدار يثبت عدم استهلاك الأرقام أو تغيير حقول التدقيق عند فشل العملية، ثم نجاح التخصيص الصحيح. هذا إصلاح اعتمادية SYS10 اللازمة لـGL14، ولا يغيّر مخططات `muhaseb-erp-analysis` أو SQL أو حواجز SYS09/GL14 أو بيانات الإنتاج. **GL14-02 تبقى جزئية؛ لا ادعاء بإنجاز مهمة جديدة قبل قبولها.** التحقق المحلي على .NET 10 Release وHTTP/JWT وOpenAPI وtraceability: **356/356 ناجح، صفر فشل** دون SQL حي؛ `backend/.artifacts/sys10-atomicity-20261102/verification.json`. يبقى اختبار SQL المعزول عبر CI الفرع والدمج في `main` مشروطًا بنجاحه.

## GL14-02 — فحص مراجع نوع المستند دون تفويض تشغيل، جزئي (2 نوفمبر 2026)

من `origin/main=a316485a7e4c20c18b40bb1d0dda171779f6c6fb` على الفرع `temp/gl14-reference-preflight-20261102`، أُضيف فحص محافظ **للقراءة فقط** يتحقق من تطابق مراجع GL14 مع سلسلة الترقيم SYS10 ومسار SYS09 في الشركة والفرع والمعرّفات وحالة النشاط وسلامة البيانات المحمّلة. أي مرجع مفقود/غير صالح أو خارج الشركة يعيد `InvalidSnapshot`، والمعطل يعيد `InactiveReference`؛ حتى المراجع المتطابقة تعيد `UnresolvedPolicy` ولا تمنح صلاحية حفظ أو اعتماد لعدم وجود سياسة معتمدة تربط رموز GL14 بنوع الترقيم ونوع SYS09. أضيف فحص إصدار متوقع لعمليات تعديل/تعطيل البطاقة مع اختبارات ذرية الرفض. [التفصيل](gl14-reference-preflight.md). التحقق المحلي .NET 10 Release وHTTP/JWT وOpenAPI وtraceability **355/355 ناجح، صفر فشل**، دون SQL حي، بصمة المصادر `c13d8b407ef2a454df29b79d1953d501f10d880091dcfe4a161a628421700e18`. نجح [CI الفرع #41511302793](https://github.com/ameenalqershi/ameen-erp/actions/runs/41511302793) **356/356، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI؛ دُمج Fast-forward إلى [main عند 2969885](https://github.com/ameenalqershi/ameen-erp/commit/2969885d3aa59dfa6abc27b03c2b660f67a39e80)، ونجح [CI الرئيسي #41512263963](https://github.com/ameenalqershi/ameen-erp/actions/runs/41512263963) **356/356، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI؛ لا تعديل لمخططات `muhaseb-erp-analysis` أو بيانات الإنتاج أو حواجز EF، وGL14-02 وSYS09 ما زالا جزئيين.

## SYS09-FG03 — تفرّد هوية مراحل الاعتماد بين المسارات، جزئي (2 نوفمبر 2026)

من `origin/main=d8c395a` على فرع `temp/sys09-preflight-amount-integrity-20261102`، أضيف فحص محافظ يرفض تكرار معرف مرحلة الاعتماد بين مسارات مختلفة ضمن لقطة SYS09 المحمّلة، بما فيها تكرار معرف مرحلة المرشح في مسار آخر. يستثني صف المرشح المخزن بالمعرف نفسه لتظل معاينة تعديل المسار ممكنة. اختبار انحدار يغطي التكرار والاستعادة والمرشح المخزن. **مجال فقط، دون تشغيل اعتماد أو حفظ SYS09/GL14**؛ لا تعديل للمخططات أو الإنتاج. [التفصيل](sys09-route-preflight.md). التحقق المحلي النهائي **353/353 ناجح، صفر فشل** مع Release وHTTP/JWT وOpenAPI وtraceability دون SQL حي؛ نجح [CI الفرع #41501441091](https://github.com/ameenalqershi/ameen-erp/actions/runs/41501441091) على commit `0df42bc` **354/354، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI، وبصمة المصادر `7033f16d2f69770db82f556b3769bec15027a9a06196c8664185b7356754e3fc`. لم يُرفع حاجز EF؛ قبول SYS09/GL14 التشغيلي ما زال محجوبًا.


## SYS09-FG04 / SYS09-CTRL01 — سلامة حقول التدقيق في لقطة تعارض المسارات، جزئي (2 نوفمبر 2026)

من `origin/main=3180f7a` وعلى فرع `temp/sys09-route-overlap-boundary-20261102`، أُضيف رفض محافظ للحقول المحمّلة غير السليمة `CreatedBy` و`CreatedAt` و`Revision` و`LastModifiedAt` قبل نتيجة فحص التعارض، لكل من المرشح وصفوف الشركة. تغطي اختبارات الانحدار الفساد والاستعادة دون تغيير بيانات أو السماح بحفظ مسار. التحقق المحلي .NET 10 Release وHTTP/JWT وOpenAPI وtraceability: **352/352 ناجح، صفر فشل**، دون SQL حي، بصمة المصادر `dd194bd614b62e023f59430b5fe5834d14111f61ecf54fd8311bfd7b0e768a9c`. [تفاصيل الحدود](sys09-route-preflight.md). نجح [CI الفرع #41492053263](https://github.com/ameenalqershi/ameen-erp/actions/runs/41492053263) على commit `55e40dd` **353/353 ناجح، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI، وبصمة المصادر نفسها. دُمج Fast-forward إلى [main عند 42d2eea](https://github.com/ameenalqershi/ameen-erp/commit/42d2eea0546483b40e17065258d51aab3bc74e40)، ونجح [CI الرئيسي #41492690670](https://github.com/ameenalqershi/ameen-erp/actions/runs/41492690670) **353/353، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI؛ تبقى SYS09 وGL14 جزئية، وحواجز EF فعالة. لم تُمس المخططات الأصلية أو الإنتاج.

## SYS09-FG03 / SYS09-CTRL01 — رفض مراحل الاعتماد المحمّلة التالفة، جزئي (2 نوفمبر 2026)

على فرع `temp/sys09-conflict-edge-tests-20261102` من `origin/main=8cfb93b` أضيف تحقق محافظ في `ApprovalRouteConflictPolicy` لرفض مراحل المسار المحمّلة الفارغة أو المكررة أو التابعة لمسار/شركة أخرى، والمراجع وأرقام المراحل والمهل غير الصالحة، قبل تقرير عدم التعارض. اختبارات جديدة للتحريف والاستعادة. اجتاز التحقق المحلي **351/351، صفر فشل** مع Release وHTTP/JWT وOpenAPI وtraceability، دون SQL حي؛ نجح [CI الفرع #41482485798](https://github.com/ameenalqershi/ameen-erp/actions/runs/41482485798) على `ad6d78d` **352/352، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI؛ بصمة المصادر `567e7ed19171e152fd3955faae93331df0fd4d4de2c2e84494841f47dc198b3e`. دُمج Fast-forward إلى [main عند 5eca417](https://github.com/ameenalqershi/ameen-erp/commit/5eca417ff14ecda86f15606a26f805d558f9d702)، ونجح [CI الرئيسي #41483478137](https://github.com/ameenalqershi/ameen-erp/actions/runs/41483478137) **352/352، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI. [التفصيل](sys09-route-preflight.md). لا تغيير لمخططات `muhaseb-erp-analysis` أو ترقية SQL أو حواجز EF، ولا قبول تشغيلي لـSYS09 أو GL14.


## SYS09-FG03/FG04 — فحص محافظ لتعارض المسارات واستبدال المراحل ذريًا (2 نوفمبر 2026)

أُضيف `ApprovalRouteConflictPolicy` كفحص نطاق/مبلغ **داخل المجال فقط**: تداخل حدود المبالغ شامل الطرفين، والفرع العام يتقاطع مع الفرع المحدد، والعملة المختلفة تعيد حالة `UnresolvedCurrency` بدل المقارنة الرقمية غير المعتمدة، وترفض اللقطات الفارغة/المكررة/العابرة للشركات. أضيف استبدال ذري لمراحل الاعتماد مع التحقق من إصدار التعديل، واختبارات الرفض دون تغيير الحالة. [الحدود والتفصيل](sys09-route-preflight.md). **لا توجد سياسة قائمة أنواع مستندات معتمدة أو تحكيم تعارض داخل معاملة SQL أو API اعتماد؛ حواجز SYS09/GL14 مستمرة.** لا تعديل للمخططات أو بيانات الإنتاج. حالة القبول **جزئية**؛ اجتاز التحقق المحلي .NET 10 Release وHTTP/JWT وOpenAPI وtraceability **350/350، صفر فشل** دون SQL حي، وبصمة المصادر `64c9616fb3de2158362fff8c5c827ee51073feddca130ba2ba86565fafdbd88a`. نجح [CI الفرع #41472681709](https://github.com/ameenalqershi/ameen-erp/actions/runs/41472681709) على [commit bfd0691](https://github.com/ameenalqershi/ameen-erp/commit/bfd0691f76b291f42976662a20fcefa287925435) **351/351، صفر فشل** مع SQL Server معزول، HTTP/JWT، OpenAPI وRelease؛ بصمة المصادر `64c9616fb3de2158362fff8c5c827ee51073feddca130ba2ba86565fafdbd88a`. هذا اختبار عدم تراجع SQL لا اختبار حفظ مسارات SYS09 المحجوبة. دُمج Fast-forward إلى [main عند c1fdda9](https://github.com/ameenalqershi/ameen-erp/commit/c1fdda9cf9567af838605db866742720c168dfb2)، ونجح [CI الرئيسي #41473610119](https://github.com/ameenalqershi/ameen-erp/actions/runs/41473610119) مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease. لا يرفع هذا حواجز حفظ SYS09/GL14 أو يثبت اعتمادًا تشغيليًا.


## SYS07-GAP01 — التحقق المحافظ من القواعد المحمّلة ولقطة التقييم (2 نوفمبر 2026)

على فرع `temp/sys07-hydrated-policy-invariants-20261102` من `origin/main=183499d`، أُضيف تحقق من رموز الشاشة/العملية القياسية وسلامة حد المبلغ وعملته في قواعد SYS07 المحمّلة من التخزين، ومنع صفوف العضوية أو الدور الفارغة في مقيّم الصلاحيات، مع أخذ لقطة واحدة من كل تعداد لتجنب تغير البيانات أثناء التقييم. أُضيفت اختبارات فساد البيانات ثم الاستعادة، تشمل رموزًا غير قياسية، حدًا بلا عملة، عملة بلا حد، مرجع دور تالفًا، وصفوفًا فارغة. اجتاز التحقق المحلي الأول **347/347**، Release وHTTP/JWT وOpenAPI وtraceability، دون SQL حي؛ نجح [CI الفرع #41454130782](https://github.com/ameenalqershi/ameen-erp/actions/runs/41454130782) على [commit 2bfa438](https://github.com/ameenalqershi/ameen-erp/commit/2bfa438b5dd18fd03a8e2433a0f0805103a0459a) **348/348، صفر فشل** مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease؛ بصمة المصادر `1eebf727684c92923e8cea4bfd38519cb293ab8e12f98e6cc6570b00e191e41f`. دُمج Fast-forward إلى [main عند 7a1ae47](https://github.com/ameenalqershi/ameen-erp/commit/7a1ae4759b516c227ce617890eb33c120346071a)، ونجح [CI الرئيسي #41455409248](https://github.com/ameenalqershi/ameen-erp/actions/runs/41455409248) **348/348، صفر فشل** مع SQL Server معزول وRelease وHTTP/JWT وOpenAPI وبصمة المصادر نفسها. [التفصيل](sys07-hydrated-policy-invariants.md). لا API جديدة أو إنفاذ صلاحيات أو ترقية SQL أو تعديل للتحليل الأصلي؛ تبقى حواجز EF وSYS07/SYS08/SYS09/GL14 جزئية.

## SYS07-GAP01 — سلامة قواعد الدور المحمّلة، جزئي (2 نوفمبر 2026)

بعد التحقق من `origin/main=34a1aae` ونجاح [CI الرئيسي #41438417630](https://github.com/ameenalqershi/ameen-erp/actions/runs/41438417630)، أُضيف فحص محافظ `SecurityRole.HasValidRuleSnapshot` لرفض قواعد دور مكررة أو ذات شركة/دور مختلفين في التقييم المباشر والمقيّم المجمّع. اختبارات جديدة تغطي لقطة سليمة، إدخال قاعدة من شركة أخرى، تكرار قاعدة، ثم استعادة اللقطة السليمة. [التفصيل](sys07-role-rule-integrity.md). محليًا: **346/346 نجاح**، Release وHTTP/JWT وOpenAPI وtraceability؛ نجح [CI الفرع #41446001239](https://github.com/ameenalqershi/ameen-erp/actions/runs/41446001239) **347/347، صفر فشل** مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease، وبصمة المصادر `71aa6892a25fdefa897c3a0033b7cbdc2aa4bdfad09f7c72e250600896d2e1af`؛ دُمج Fast-forward إلى [main عند ef4ec15](https://github.com/ameenalqershi/ameen-erp/commit/ef4ec15511368c19851dffbcccfdb07acfc5ef47) ونجح [CI الرئيسي #41446931901](https://github.com/ameenalqershi/ameen-erp/actions/runs/41446931901) **347/347، صفر فشل** بنفس بصمة المصادر. لا صلاحيات تشغيلية جديدة، ولا تعديل للمخططات أو SQL، وتبقى حواجز حفظ SYS07/SYS08/SYS09/GL14. المتبقي كتالوج العمليات المعتمد وإدارة الأدوار/الروابط المدققة ومنع التصعيد الذاتي وقبول HTTP/JWT الفعلي.

## SYS07-GAP01 — رفض عناصر قواعد الأدوار الفارغة، جزئي (2 نوفمبر 2026)

على فرع `temp/sys07-null-draft-validation-20261102` من `main=d130c44`، أضيف تحقق صريح من عنصر قاعدة `null` في `SecurityRole.ReplaceRules` قبل تغيير حالة الدور؛ يمنع خطأ NullReferenceException غير المنضبط مع الحفاظ على ذرية الاستبدال. أضيف اختبار يمرر قاعدة صحيحة ثم عنصرًا فارغًا ويتحقق من رفض `invalid_role_rules` دون تغيير الإصدار أو القواعد. التحقق المحلي النهائي **345/345** على Release وHTTP/JWT وOpenAPI دون SQL حي؛ يبقى اختبار CI مع SQL معزول مطلوبًا قبل الدمج. لا صلاحيات تشغيلية جديدة، ولا تعديل للتحليل الأصلي، ولا رفع لحواجز EF.

## SYS07-GAP01 — توحيد مدخلات تقييم الدور، اعتماد جزئي (2 نوفمبر 2026)

استُكمل التعديل المحلي `dcd75c2` دون التداخل مع شجرة العمل الأصلية: أُعيد تطبيقه على أحدث `main=03eeb1a` في الفرع المعزول `temp/sys07-normalization-integrate-20261102` عند `94bd3ea`. أصبح تقييم `SecurityRole.Permits` يطبّع المسافات وحالة الأحرف لرموز الشاشة والعملية، بما يتفق مع مقيّم لقطة الأدوار، مع اختبار انحدار. نجح التحقق المحلي **345/345، صفر فشل**، وRelease وHTTP/JWT وOpenAPI وtraceability، دون SQL حي؛ [CI الفرع #41433033058](https://github.com/ameenalqershi/ameen-erp/actions/runs/41433033058) **346/346 ناجح، صفر فشل** على `94bd3ea` مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease؛ بصمة المصادر `d958a9edc6ccd15bfe93d54519ec64706d6839425c0ce82c41a580c8b4793c02`. دُمج Fast-forward إلى [main عند 053ca12](https://github.com/ameenalqershi/ameen-erp/commit/053ca1299f559b640e596beccc737c152c572f67) ونجح [CI الرئيسي #41434102018](https://github.com/ameenalqershi/ameen-erp/actions/runs/41434102018) **346/346، صفر فشل** على SHA نفسه، مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease. حدثت مهمة SYS07-01 في Notion بحالة جزئية؛ لا تفعيل صلاحيات جديد. لم تتغير الحقول والمخططات الأصلية ولا حواجز EF، وما زال SYS07/SYS08/SYS09/GL14 جزئيًا. [نقطة الاستئناف](automation-resume.md).

## SYS07-GAP01 — سلامة لقطة الأدوار، قبول محلي جزئي (2 نوفمبر 2026)

أُضيف رفض محافظ للقطات الأدوار الناقصة أو العابرة للشركات وروابط الأدوار المكررة قبل تقييم المنح، بحيث لا يؤدي فقدان دور مانع إلى سماح غير مقصود. اختبارات مجال جديدة للرفض وقبول لقطة سليمة. [التفصيل](sys07-role-snapshot-integrity.md). التحقق المحلي .NET 10 Release وHTTP/JWT وOpenAPI وtraceability **345/345 ناجح، صفر فشل** دون SQL حي؛ نجح [CI الفرع #41430688379](https://github.com/ameenalqershi/ameen-erp/actions/runs/41430688379) **346/346، صفر فشل** مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease. بصمة المصادر `1133ca97eb94db6fafe18be8fd41a860fc9afa5c804ff6e5ec508bae0a216513`. تحقق CI الرئيسي بعد الدمج ما زال مطلوبًا. لا تعديل للتحليل الأصلي، ولا إنفاذ صلاحيات جديد أو واجهات أدوار، وتبقى SYS07/SYS08/SYS09/GL14 جزئية.


## SYS07-GAP01 — تحرير قواعد الأدوار الذري والتحقق من الإصدار، اعتماد جزئي (2 نوفمبر 2026)

من نقطة الاستئناف main=a29597f و[CI الرئيسي الناجح #41418558320](https://github.com/ameenalqershi/ameen-erp/actions/runs/41418558320) بدأ فرع temp/sys07-role-policy-atomic-edits-20261102 بعد فحص نظافة شجرة العمل والعمليات. أضيفت SecurityRole.ReplaceRules لاستبدال مجموعة قواعد الدور **بصورة ذرية داخل المجال**، والتحقق من التفرد بعد تطبيع الرموز، ورفض حدود مبالغ/عملات غير صالحة قبل تغيير حالة الدور. أضيفت واجهات تحرير/تعطيل مع إصدار متوقع لمنع التعديلات القديمة، واختبار استقلال POST وEXPORT وCOST ضمن مقيّم الدور. [تفاصيل وحدود التنفيذ](sys07-role-policy-atomic-edits.md).

التحقق المحلي على .NET 10 Release وHTTP/JWT وOpenAPI وtraceability: **345/345 ناجح، صفر فشل**، دون SQL حي. **لا توجد إدارة أدوار API أو تدقيق/حفظ معتمد أو إنفاذ صلاحيات جديد**؛ حواجز EF ما زالت فعالة. تبقى SYS07 وSYS08 وSYS09 وGL14 جزئية. نجح [CI الفرع #41422155947](https://github.com/ameenalqershi/ameen-erp/actions/runs/41422155947) على [commit e4fbec9](https://github.com/ameenalqershi/ameen-erp/commit/e4fbec9be5fe9fa9173afc882c9cf518c7a0e984): **346/346 ناجح، صفر فشل** مع SQL Server معزول وHTTP/JWT وOpenAPI وRelease، وبصمة المصادر bb90be5e2d8f79dc0a30cee30d4663a98e02f7572c3350083a532bc7f19c59d9. لم تُضف ترقية SQL جديدة؛ الاختبارات الحية تؤكد عدم التراجع فقط. نجح كذلك [CI توثيق الفرع #41422723799](https://github.com/ameenalqershi/ameen-erp/actions/runs/41422723799)، ثم دُمج Fast-forward إلى [main عند SHA 414f256](https://github.com/ameenalqershi/ameen-erp/commit/414f2564649a89c6873fb00ad8f5273f2349ab06) ونجح [CI الرئيسي #41423037624](https://github.com/ameenalqershi/ameen-erp/actions/runs/41423037624) **346/346 ناجح، صفر فشل**. سجل الدليل النهائي في verification-results.json، ولم تتغير حالة قبول SYS07 الجزئية. لا تعديل للمخططات الأصلية أو الإنتاج.

## SYS07/SYS08 — ربط العضوية بالدور والمقيّم المحافظ، أساس جزئي (2 نوفمبر 2026)

بعد التحقق من نظافة main وتطابقه مع origin/main عند SHA 7aacbe6d058e925560732b247c4164ac0f703799 وعدم وجود عملية Git أو dotnet نشطة، بدأ فرع temp/sys07-membership-role-assignment-20261102. أُضيف ربط عضوية-دور داخل الشركة بمراجع مركبة وفهرس فريد وترقية SQL 030 ذرية، ومقيّم مستقل يطبق المنع الصريح بين الأدوار وحدود الفرع والمبلغ والعملة مع رفض العضوية المعطلة. لا تمنح هذه البنية صلاحيات فعلية: كتابة الأدوار والروابط ومسارات SYS09 وGL14 محجوبة، ولا توجد واجهة لإدارة الأدوار أو قبول وظيفي. [التفصيل والحدود](sys07-membership-role-assignment.md).

التحقق المحلي: **344/344 ناجح، صفر فشل**، Release بلا أخطاء أو تحذيرات، HTTP/JWT وOpenAPI وtraceability. نجح [CI الفرع #41417654362](https://github.com/ameenalqershi/ameen-erp/actions/runs/41417654362) على [commit b6cc959](https://github.com/ameenalqershi/ameen-erp/commit/b6cc9590458acef53668d90c411c206e98577aab): **345/345 ناجح، صفر فشل** مع SQL Server معزول (SQL 030 فشل/تراجع/إعادة، FK وفهرس فريد وحراس الحفظ)، HTTP/JWT وOpenAPI وRelease. نجح أيضًا [CI الفرع النهائي #41418066078](https://github.com/ameenalqershi/ameen-erp/actions/runs/41418066078) على commit 3b6f877 بعد توثيق الأدلة، ثم دُمج Fast-forward إلى main عند SHA 3b6f8776fb7827d21c05494480aa41cbe0563066؛ يُتحقق من CI الرئيسي بصورة مستقلة. بصمة المصادر a47d5d1c3322248597fc5314fe8d5793a3d66821d981723c1a91454647d46e06. [نقطة الاستئناف](automation-resume.md). لا تعديل على muhaseb-erp-analysis أو بيانات الإنتاج.

## SYS07 dependency for SYS09/GL14 — structural role catalog, partial (2 November 2026)

On a clean `main=e6a500b`, branch `temp/sys07-role-foundation-20261102` adds the SYS07 `SecurityRole` / `SecurityRoleRule` domain, tenant EF model, atomic SQL 029 and a tenant-scoped FK from SYS09 approval stages to roles. It validates explicit deny and amount/currency caps in the domain but **does not grant permissions**: all role/rule writes remain blocked, and SYS09/GL14 remain blocked. No roles or policies are seeded and the original analysis is unchanged. [Scope and blockers](sys07-role-foundation.md).

Local Release + domain/EF + HTTP/JWT + OpenAPI verification: **342/342 passed, 0 failed** without live SQL. [Branch CI #41400101494](https://github.com/ameenalqershi/ameen-erp/actions/runs/41400101494) on [code commit d3aa045](https://github.com/ameenalqershi/ameen-erp/commit/d3aa045b170183a377769757eb2bef9667dc428a) passed **343/343, 0 failed**, including disposable SQL 029 rollback/retry, branch and role FK rejection, HTTP/JWT, OpenAPI and Release. Source SHA256 `661549d14ff032b646ffff22a1d8eecab1434f94c03f884dc4068ad91f4b2a6a`. No SYS07, SYS09 or GL14 functional completion claim. [Resume](automation-resume.md).

## SYS09-FG01/FG02/FG03/FG04 — أساس اعتمادية GL14، جزئي وغير مقبول (2 نوفمبر 2026)

بعد التحقق من `main=f00510e` ونظافة شجرة العمل وعدم وجود عمليات Git نشطة، بدأ فرع `temp/sys09-approval-route-foundation-20261102` لتقليل مانع GL14 دون إعادة عمل GL14-01. أُضيف كيان `ApprovalRoute` وبنوده `ApprovalRouteStage` وتهيئة EF وترقية `028_approval_route_foundation.sql`: حدود `decimal(20,6)`، فرع وعملة بمراجع مركبة للشركة، مرحلة فريدة، رقم إصدار وتدقيق، وFK حقيقي من GL14 إلى SYS09. لا بيانات ابتدائية ولا تعديل للمخططات الأصلية. [التفصيل والمانع](sys09-approval-route-foundation.md).

**لا توجد أدوار SYS07 دائمة ولا قائمة أنواع مستندات معتمدة ولا سياسة تعارض نطاق/مبلغ**؛ لذلك يمنع حارس EF كل كتابة SYS09 ويظل حارس GL14 قائمًا. لا API أو اعتماد متعدد المراحل أو قبول لهذه المهام؛ التقدم **أساس بنيوي فقط**. نجح التحقق المحلي `339/339`، صفر فشل، مع build Release وHTTP/JWT وOpenAPI وtraceability؛ فشل تشغيل CI الأول [#41394192566](https://github.com/ameenalqershi/ameen-erp/actions/runs/41394192566) بسبب قوس ناقص في CHECK SQL 028 وصُحح في commit `temp: 8722f4d`. نجح [CI #41394461191](https://github.com/ameenalqershi/ameen-erp/actions/runs/41394461191) على [8722f4d](https://github.com/ameenalqershi/ameen-erp/commit/8722f4d72a521afcd7c1ce803728dc4475885e2d) **340/340 نجاح، صفر فشل** مع SQL Server معزول (فشل DDL والتراجع والإعادة وFK)، HTTP/JWT وOpenAPI وRelease. بصمة المصادر `3f257d87e69925921d1449bfeaca3c07d220bb3142483a5379dac9f9f6b0fd8a`. [نقطة الاستئناف](automation-resume.md).


## GL14-01 — أساس البيانات الآمن، غير مكتمل وظيفيًا (2 نوفمبر 2026)

بعد التحقق من `main=f70f80cb` وCI الناجح `41383596304`، تبيّن أن GL02-T01 مكتملة وأن GL02-A07/GP02/GP03/C01/GP04/GP05 محجوبة باعتماديات الطباعة والاستيراد والتسوية والدفاتر المساعدة؛ لذلك انتقل العمل المستقل التالي داخل GL إلى GL14 وفق خطة الاعتماديات. أضيفت بنية GL14 للحقول F01–F13 في EF وترقية SQL 027 ذرية مع تفرد الرمز وعزل الشركة ومرجع الترقيم، وحارس يمنع كتابة أي نوع مستند قبل وجود مسار اعتماد SYS09 فعلي. **لا يوجد API أو ربط GL03 أو قبول GL14 مكتمل**، ولم يُختلق مسار اعتماد. [تفاصيل البنية والمانع](gl14-01-foundation.md)؛ [نقطة الاستئناف](automation-resume.md).

فحوص المجال والنموذج محليًا: **336/336 نجاح، صفر فشل** على .NET 10 دون SQL حي. نجح [CI #41389013770](https://github.com/ameenalqershi/ameen-erp/actions/runs/41389013770) على [a336d36](https://github.com/ameenalqershi/ameen-erp/commit/a336d36337c0d2ce27943a999b247e4ce5a56104): **337/337 نجاح، صفر فشل** مع SQL Server معزول، اختبار ذرية وإعادة ترقية 027، وHTTP/JWT وOpenAPI وRelease. بصمة المصادر `0c091f534aa82c47fd5b583dd98c6971785e35c5f02a12ef3b0b6ba81a3b0a55`. حُدّثت المصفوفات بحالة `partial` فقط. لا تعديل على `muhaseb-erp-analysis`، ولا تغيير بيانات إنتاج.

## GL02-T01 — قبول إيجابي للافتتاح العام (2 نوفمبر 2026)

اكتمل اختبار قبول مستقل للدورة المترابطة: إنشاء افتتاح متزن بمبلغ 175.25 لكل طرف، معاينة الأثر بلا كتابة، تقديم واعتماد وترحيل، ثم التأكد من مستند وقيد واحد وOutbox واحد وثبات المبالغ وعدم السماح بإعادة تقديم المرحّل. لا يمثل ذلك وظيفة محاسبية جديدة أو إغلاقًا لشاشة GL02. [تفصيل القبول](gl02-t01-acceptance.md).

[commit 713810c](https://github.com/ameenalqershi/ameen-erp/commit/713810cdb4e933e824162048aedbd2dea91ce14b) رُفع إلى `main` بعد نجاح [CI #41383316448](https://github.com/ameenalqershi/ameen-erp/actions/runs/41383316448) على SHA نفسه: **334/334** ناجح مع SQL Server معزول وHTTPS/JWT وOpenAPI وRelease؛ التحقق المحلي دون SQL **333/333**. لم تتغير مخططات أو حقول `muhaseb-erp-analysis`. سجل الاستئناف في [automation-resume.md](automation-resume.md)؛ تظل GL02-A07/GP02 للطباعة وGP03 للاستيراد وC01/GP04 للتصفير وGP05 للمساعدات فجوات معلقة بحسب الاعتماديات.


## GL06-GP02 — سجل الدفعات الدائم مع ترقية SQL 025 (28 أكتوبر 2026)

أُضيف رأس دفعة مستقل `JournalProcessingBatch` به مفتاح مميز على مستوى الشركة، بصمة حمولة الطلب، هوية المنفذ، حالة تسجيل العناصر، إصدار تعديل **int** ووقت إنشاء/آخر تعديل من الخادم. إضافة `BatchKey` **اختيارية** إلى `ProcessJournalsRequest` تحفظ توافق العقود القديمة. تفشل إعادة استخدام المفتاح لحمولة مغايرة. لكل مستند معاملة مستقلة كما في الضابط الأصلي؛ يخزن نجاح/رفض/إخفاق المستند في معاملة تشغيلية منفصلة، وتبقى محاولات الإخفاق السابقة في `AuditEvent` غير القابل للتعديل. يمكن إعادة محاولة الإخفاق التقني بمفتاح الدفعة الأصلي؛ أما النجاح ورفض التحقق المسجل فلا يعاد تنفيذهما.

المسار `GET /api/v1/companies/{companyId}/journals/processing-batches/by-key/{batchKey}` يعيد النتيجة وإصدارها بشرط تطابق المنفذ مع هوية JWT وصلاحية القراءة لكل قيد. لا يعرض تفاصيل فرع/شركة غير مخول بها. لا يرقّي سجل الفشل وحده إلى ترحيل مالي، ولا يدّعي نجاح الدفعة حين تفشل بعض العناصر.

ترقية `025_journal_processing_batches.sql` تنشئ سجلات تشغيلية فارغة، وتُختبر ذريتها وتكرارها. [CI #40738969586](https://github.com/ameenalqershi/ameen-erp/actions/runs/40738969586) على SHA `3b192e74a164ee173b75646551206746a16b9801`: **321/321 نجاح** مع SQL Server، رفض وتراجع حقيقي، ثم إعادة ناجحة وفصل أرقام الإصدار، وHTTPS/JWT وOpenAPI. هذه النتيجة لا تشهد بتشغيل عامل Outbox على VPS ولا بتوجيه مصادر محاسبية غير منفذة. لا تعديل للمخطط الأصلي.


## دفعة العشرين — 20/20 مقبولة، 27 أكتوبر 2026

اكتمل تنفيذ مهام GL13 الإحدى عشرة للحقول F01–F17 والأفعال والضوابط والقبولين؛ [العقد](financial-mappings.md). تشمل خريطة وصيغًا آمنة، مراجعة الأستاذ المرحّل وغير المصنف، لقطات لا تتغير مع تعديل التعريف، وربط استخدام الحساب وقفل بنيته. ترقية 024 تحفظ الأثر السابق ولا تنشئ تصنيفات. لا يعد ذلك تنفيذ تقارير RPT03/RPT04 كاملة.

اجتاز التحقق المحلي 319/319 قبل الإرسال. [CI #40436818618](https://github.com/ameenalqershi/ameen-erp/actions/runs/40436818618) على [8a987d0](https://github.com/ameenalqershi/ameen-erp/commit/8a987d07bb3e4c7edff22222c70d3044be34902d): **320/320 ناجح، صفر فشل** مع SQL Server وHTTPS/JWT وOpenAPI وترقية 023→024 ذرية وقابلة للإعادة. أغلقت المهام العشرون في Notion: SYS10 تسع وGL13 إحدى عشرة. جميعها تطوير جديد، دون إعادة عد الحزم الأم أو مهام الجولات السابقة.

## دفعة 20 مهمة إضافية — المرحلة الأولى SYS10، 27 أكتوبر 2026

أُنجز تنفيذ تسع مهام SYS10 الدقيقة (FG01–FG03، OP01–OP03، CTRL01، AC01، AC02) وربط الترقيم بالمستندات الموجودة، وترقية 023، وحماية الحجز والعزل والتزامن والإلغاء. أُكملت فجوة نطاق الأرقام المخصصة في GL06 والروابط التاريخية عند نقل سنة المسودة. [العقد](number-series.md) و[المهام العشرون](twenty-numbering-mapping-tasks.md).

نجح التحقق المحلي 303/303 على .NET 10 وبناء بلا أخطاء أو تحذيرات؛ نجح [CI #40434281835](https://github.com/ameenalqershi/ameen-erp/actions/runs/40434281835) على 3aef1b43: **304/304** مع SQL وHTTPS/JWT، وأغلقت المهام التسع في Notion. المرحلة الثانية 11 مهمة GL13 للحقول وبنود الربط والصيغ ومنع الدورات وتكرار الحسابات وفحص غير المصنف والاستخدام والتدقيق. لا تعد مهام GL04/GL03 المقبولة سابقًا ضمن هذه العشرين، ولا تُعد الحزم الأم أو GAP المتداخلة مرتين.


## GL03-FG08 — رقم إصدار صحيح ووقت تعديل خادمي، 27 أكتوبر 2026

- جرى فصل Revision الصحيح وLastModifiedAt عن UUID Version في تفاصيل اليومية وقوائمها والمعالجة؛ المصدر GL03-F25–F28 مطابق لنوعه، دون استبدال هوية التزامن أو الحقول الأصلية.
- تعديل القيد وانتقالات حالته وروابط العكس تزيد الإصدار مرة لكل أمر ناجح. الإعادة والرفض وفشل الحفظ لا تزيده؛ مصدر المخزون يحافظ على الذرية، وتظل حقول GL02 المتخصصة متوافقة مع سلوكها السابق.
- ترقية SQL 022 تعيد بناء الإصدار والوقت من التدقيق وروابط العكس دون تعديل الأثر المالي أو UUID؛ تشمل الاختبارات فشل DDL والتراجع وإعادة التطبيق وحفظ التاريخ. [تفاصيل الحقول والحدود](gl03-audit-fields.md).
- التحقق المحلي 291/291 نجح دون SQL، وبناء بلا أخطاء أو تحذيرات. نجح [CI #40424701950](https://github.com/ameenalqershi/ameen-erp/actions/runs/40424701950) على `e0db6cb`: **292/292** مع SQL Server وHTTPS/JWT وترقية 022. أُغلقت GL03-FG08 في Notion وحُفظ تقرير CI وعقد OpenAPI من حزمة التشغيل نفسها.
- ما زالت أنواع القيود والمرفقات والمشروع والطرف والعكس المجدول فجوات GL03؛ لا إعلان اكتمال الشاشة أو القسم.


## GL04-FG02 وGL04-FG04 وGL04-GP01 — استكمال الحقول، 27 أكتوبر 2026

- تاريخ النهاية `GL04-F07` أصبح اختياريًا في عقود الإنشاء والتعديل والقراءة وفي SQL 021، مع حفظ المواعيد والقيود السابقة ودون تاريخ نهاية مصطنع. بقية حقول FG02 للدورية والبداية وManual/Scheduled مختبرة ضمن الحزمة.
- استُكملت بوابة حقول التدقيق FG04 لاختبار ترقية رقم الإصدار الصحيح وآخر تعديل من التاريخ، وتراجع التعديل والتعطيل عند فشل التدقيق، وإعادة متزامنة لا تزيد الإصدار أو تكرر الحدث. إصلاح تنظيف اختبار ترقية 020 يعالج فشل CI السابق دون تخفيف قيد ربط الفرع.
- تصحيح OpenAPI لمسح run-due وحفظ أدلة CI؛ التوليد Scheduled نفسه تطوير سابق يُتحقق منه، وليس ميزة جديدة في هذه الجولة. [مصفوفة المتطلبات والاعتماديات](gl04-field-completion.md).
- **التحقق المحلي:** .NET SDK 10.0.403، بناء بلا تحذيرات أو أخطاء، **289/289** ناجحة دون SQL حي. يشمل HTTP الاختباري وOpenAPI الفعلي؛ نجح [CI #40422957741](https://github.com/ameenalqershi/ameen-erp/actions/runs/40422957741) على `035436e`: **290/290** مع SQL وHTTPS/JWT، وأُغلقت المهام الدقيقة الثلاث في Notion.
- **المتبقي:** GL04-FG01/GL04-F02 لسياسة الفرع الشرطي، وGL04-GP02 للقوائم الإضافية غير المعتمدة؛ لا يُعلن إغلاق الشاشة أو القسم، ولا تفعيل المجدول في VPS. المخططات الأصلية لم تتغير.


## GL04-F07 — تاريخ النهاية الاختياري (27 أكتوبر 2026)

اكتمل وفق `muhaseb-erp-analysis/data/fields.csv` جعل `EndsOn` اختياريًا (`DateOnly?`)، ودعم توليد القالب بلا نهاية محددة مع الحفاظ على انتهاء القوالب التي تحدد تاريخًا. ترقية SQL `021_recurring_optional_end.sql` ذرية، تحفظ القوالب السابقة، وتقبل `NULL` مع منع تاريخ أقل من البداية؛ اختبارات إنشاء/تعديل/SQL/HTTPS/JWT. [CI #40421699088](https://github.com/ameenalqershi/ameen-erp/actions/runs/40421699088): **286/286 نجاح**. `GL04-F02` للفرع الشرطي ما زال غير مكتمل ولا يشهد ذلك باكتمال شاشة GL04.


## GL04-GP01 — التوليد المجدول للمسودات (27 أكتوبر 2026)

- أُضيف وضع `Scheduled` (إلى جانب `Manual`) مع واجهة `POST /recurring-journals/run-due` وخدمة مضيف دورية مفوضة اختيارية. يبدأ التوليد عند استحقاق موعده فقط، ويعيد النتائج لكل فترة، ويمنع التكرار حتى بعد إلغاء المسودة، ولا ينفّذ الموافقة أو الترحيل. [دليل التشغيل والقيود](gl04-scheduled-generation.md).
- SQL migration `019_recurring_schedule.sql` ذرية وقابلة للإعادة، وتبقي تاريخ القوالب القديمة. تفصل الصلاحيات حسب الشركات والفروع، ولا تعمل خدمة المضيف حتى يفعّلها مدير النشر بهوية مستخدم عضو ومفوض.
- [CI #40418027090](https://github.com/ameenalqershi/ameen-erp/actions/runs/40418027090) على SHA `502932ba2a5e3287e6d4d8696e8510d750be1f0f`: **284/284 اختبارًا ناجحًا، صفر فشل** (تطبيق، SQL Server، HTTPS/JWT، migration 018→019، OpenAPI). لم تتغير مخططات `muhaseb-erp-analysis/` الأصلية.
- شُغّلت الاختبارات على بيئة CI مع خدمة المضيف الاختيارية غير مفعلة؛ **لا دليل على تفعيلها في VPS الإنتاجي**. تظل GL04-FG01 (الفرع الشرطي) وGL04-GP02 (دوريات إضافية غير محددة) مفتوحتين، ولا تُعلن شاشة GL04 مكتملة بالكامل.


## تسليم دفعة GL الثالثة: 20 مهمة دقيقة (27 أكتوبر 2026)

[جدول اعتماد 20 مهمة](gl-third20-task-acceptance-2026-10-27.md): GL02=4، GL05=5، GL06=4، GL07=7، مختلفة عن جولتي العشرين السابقتين. **كود جديد في هذه الجولة:** GL02-GP01 افتتـاح أجنبي وفق أحدث سعر نافذ، ترقية SQL 018 دون تعديل قيم التاريخ؛ واختبار دورة GL07 المضافة في [commit 595b7dc](https://github.com/ameenalqershi/ameen-erp/commit/595b7dc3632bde7dd71ea55d4f9326c70127c9ea). 19 مهمة أفعالها أو اختباراتها موجودة قبل الجولة وتم تدقيقها والقبول وفق نطاقها. [CI 40415280131](https://github.com/ameenalqershi/ameen-erp/actions/runs/40415280131) **283/283 نجاح**. بقية الاعتماديات وموانع الإغلاق في التقرير؛ لا إعلان اكتمال أي شاشة.


## GL02-GP01 — عملة افتتاح أجنبية وترقية 018 (27 أكتوبر 2026)

- تم تنفيذ دعم العملة الأجنبية بآخر سعر نافذ، والتحقق من العملة الموحدة بين رأس المستند والسطور والاحتفاظ بلقطة سعر التحويل والأرصدة الوظيفية وعكسها التاريخي دون إعادة تسعير.
- ترفع ترقية SQL `018_general_opening_foreign_currency.sql` قيد تثبيت سعر الافتتاح إلى سعر موجب تاريخي، مع تحقق الذرية وإعادة التشغيل والمحافظة على قيود 017 القديمة.
- [CI #40414352534](https://github.com/ameenalqershi/ameen-erp/actions/runs/40414352534) على SHA `d571bcd2f3a6c68d45526c378bd5db9faa87dfba`: **282/282 ناجحة، 0 فشل** مع SQL Server وHTTPS/JWT وOpenAPI والتحليل الأصلي دون تعديل. [تطوير](https://github.com/ameenalqershi/ameen-erp/commit/10f0ee812e10896c5afac0610f130af44bd1f16c) و[تصحيح اختبار السعر القديم](https://github.com/ameenalqershi/ameen-erp/commit/d571bcd2f3a6c68d45526c378bd5db9faa87dfba).
- أغلقت الفجوة الدقيقة GL02-GP01 وحقلي GL02-F08 وF09، لا الشاشة كلها؛ الطباعة SYS12 والاستيراد SYS13 والتسويات SAL03/PUR03 وGL10 لا تزال اعتماديات.


## دفعة 20 مهمة إضافية — تدقيق 27 أكتوبر 2026

أغلقت 20 مهمة دقيقة مختلفة عن الجولة السابقة: GL01 (5)، GL04 (7)، GL05 (4)، GL06 (4). **تطوير جديد واحد هو GL06-GP03**؛ 19 مهمة كانت مطورة واختباراتها قائمة قبل الجولة، وتمت مراجعتها وتوثيق نتائجها في Notion. [الجدول والأدلة](notion-next20-task-acceptance-2026-10-27.md). [CI #40412026128](https://github.com/ameenalqershi/ameen-erp/actions/runs/40412026128) **281/281 نجاح** مع SQL وHTTPS/JWT ونجاح traceability/progress. أُعيد فتح GL04-FG01 (فرع شرطي غير محسوم) ولم يحتسب. دون تعديل المخططات المرجعية؛ لا يعني اكتمال أقسام GL.


## إنجاز GL06-GP03 — نطاق الأرقام النصي (27 أكتوبر 2026)

- نطاق بحث نصي آمن `numberRange` للأرقام المحاسبية من نفس السنة المالية، ضمن `periodId` المحدد وصلاحيات الفروع والحالة والمصدر، مع منع النطاق المعكوس والرقم الخاطئ وإدخال المرشحات المتعارضة. بقيت حقول `sequenceFrom/sequenceTo` القديمة متاحة. [تفصيل العقد](gl06-text-number-range.md).
- [الدفعة البرمجية](https://github.com/ameenalqershi/ameen-erp/commit/41190e61988ca6212506524911ef981803cb4c41) ثم [تصحيح البناء](https://github.com/ameenalqershi/ameen-erp/commit/13d1484e27cd7402402290ad98535bec6bb4541f). [CI 40411306590](https://github.com/ameenalqershi/ameen-erp/actions/runs/40411306590): **281/281** اجتازت، SQL Server وHTTPS/JWT. لم تتغير ملفات التحليل المرجعية.
- جرى تحديث traceability + المصفوفات المولدة وتصحيح ملخص PROGRESS الآلي؛ تبقى فجوات GL06-GP01 وGL06-GP02 مفتوحة ولا يتم الإعلان عن اكتمال GL06.


## اعتماد 20 مهمة Notion دقيقة — 27 أكتوبر 2026

الدفعة: GL01 (5) وGL02 (5) وGL03 (10). [مصفوفة قبول العشرين](notion-20-task-acceptance-2026-10-27.md). **تطوير كود جديد واحد** GL03-A01 للبحث عبر journals، و**19 تسوية حالة تنفيذ سابق** بعد مراجعة الأدلة. [CI #40409584891](https://github.com/ameenalqershi/ameen-erp/actions/runs/40409584891) اجتاز **280/280** على main عند `46afe78e98dcbf4dfb02b7eabfd04444d28e4c2b`. لا يعني اكتمال 20 مهمة اكتمال شاشة أو قسم أو تطبيق. العملات الأجنبية والطباعة والاستيراد والمطابقة الشاملة وأعمال GL14 ما زالت فجوات. التزم الأصل التحليلي بلا تغيير.


## تنفيذ مهام Notion — GL02-A01 (2026-10-27)

- **تم تنفيذ GL02-A01 بالكامل في نطاق «بحث + جديد»**: البحث الآمن عن افتتاحات GeneralOpening برقم الوثيقة والوصف والمرجع الخارجي ومرجع الميزان السابق، بعد ترشيح الشركة والفروع والحالة والمصدر وقبل الترقيم الصفحي. كان إنشاء المسودة موجودًا واكتملت إضافة البحث النصي واختباراته في [temp: complete GL02-A01](https://github.com/ameenalqershi/ameen-erp/commit/321806e688be942d778a6cb43e0e213722c46a67).
- **التحقق الفعلي:** نجح [GitHub Actions CI run 40407738776](https://github.com/ameenalqershi/ameen-erp/actions/runs/40407738776) للـSHA نفسه: **279/279 اجتازت، 0 فشل**، مع SQL Server وHTTPS/JWT وفحص مطابقة المخططات وتوليد OpenAPI، وفق سجل التشغيل. لا يُعد هذا تشغيلًا محليًا في جلسة ChatGPT.
- **تحديث Notion:** GL02-A01 مكتمل بعد نجاح CI. سُوّيت GL02-A02/A03/A04/A08 إلى مكتمل اعتمادًا على التنفيذ السابق ودليل CI 278/278 في [run 40381093687](https://github.com/ameenalqershi/ameen-erp/actions/runs/40381093687). لا يجوز احتساب هذه التسوية كميزات برمجية جديدة.
- **المتبقي:** GL02-GP01 العملات الأجنبية، GL02-GP02 الطباعة واعتمادية SYS12، GL02-GP03 الاستيراد واعتمادية SYS13، GL02-GP04 سياسة التصفير والقطع، GL02-GP05 التكامل مع دفاتر الذمم والمصادر غير المنفذة. لا تعلن شاشة GL02 أو قسم GL مكتملين. التحليل الأصلي لم يتغير.
- **نقطة الاستئناف:** تحقق من اعتمادية العملة الأجنبية والمراجع المؤرخة، ثم نفذ المهمة الممكنة التالية كاملة مع اختبارات البيانات والـHTTP والمصادقة؛ أبقِ المهام غير المستوفاة للبوابة مفتوحة.


هذا هو مدخل الاستكمال لأي وكيل. طلب المستخدم: باك إند ASP.NET Core APIs فقط، مع الالتزام بالتحليل والمخططات؛ لا تبدأ واجهة أمامية. تاريخ آخر تحديث موثق 2026-10-27. لا تُفسَّر نسبة الحقول أو نجاح الاختبارات على أنها اكتمال نظام ERP أو تطابق مثبت مع المتكامل.

## أسلوب العمل المعتمد طوال المشروع

توجيه المستخدم المستمر: تطوير سريع على دفعات وظيفية كبيرة ومترابطة وفق التحليل، بدل التوقف عند تحسينات صغيرة متفرقة. لكل دفعة: مراجعة الشاشات والاعتماديات، تنفيذ الملفات المستقلة وربط الصلاحيات والتدقيق والتزامن والإقفال، ثم اختبارات تطبيق وSQL وHTTP مناسبة، وتحديث مصفوفة الحقول والنسب والتقرير وOpenAPI ونقطة الاستئناف. لا تُختصر اختبارات الأثر المالي أو تُخترع سياسات محاسبية بحجة السرعة. يُرفع الكود عند طلب المستخدم، ثم يستمر التنفيذ دون طلب تأكيد متكرر. يُذكر المنجز وحدوده بدقة ولا تُساوى تغطية الحقول بجاهزية ERP.

## القسم النشط وقاعدة الاستمرار

**الأستاذ العام GL هو القسم النشط حتى إغلاق فجواته الممكنة.** توجيه المستخدم الأحدث: إنجاز أقسام كاملة وعدم التنقل بينها إلا لضرورة موثقة. ثُبت في AGENTS.md. لا تبدأ ميزات مخزون أو تقارير مستقلة جديدة. [خطة إغلاق GL01–GL14 والاعتماديات](gl-completion.md)؛ عند تعذر وظيفة لاعتمادية خارجية سجلها وأكمل المستقل داخل GL. لم يكتمل قسم GL بعد.

## نقطة الاستئناف

**الدفعة الحالية: تعديل كامل لرأس المسودة والسطور في GL02/GL03، دون ترقية SQL (017).** Header اختياري متوافق مع الطلبات السابقة: الفرع وتاريخ الوثيقة والأثر والمرجع الخارجي، وتاريخ بدء التشغيل للافتتاح. اشتراط صلاحية الفرعين، والفترة الحالية والجديدة مفتوحتان، وإعادة ربط الفترة من التاريخ. يحافظ على الرقم داخل السنة ويخصص رقم السنة الجديدة عند نقل المسودة، مع إعادة فحص الحسابات والمراكز وأسعار الصرف وتحضير السطور. الرأس والسطور والتسلسل والتدقيق والإيصال في معاملة واحدة. المصادر المولدة ومسودات العكس لا تقبل تغيير الرأس.

نجحت 277/277 محليًا دون SQL؛ تسعة اختبارات تطبيق جديدة وفحص OpenAPI حقيقي من خريطة الإنتاج دون اتصال SQL. أضيف اختبار SQL للتراجع عند فشل التدقيق وإعادة ستة طلبات متزامنة وحماية الرأس المرحّل، وفحص HTTPS لتعديل الافتتاح. تم توليد OpenAPI من المضيف، والمخططات الأصلية اجتازت فحص البصمات. تحقق نجاح CI للدفعة عند `963aa165fcbf7073acf33df75e5fe4649e7a9449` على main، run `40381093687`: **278/278** مع SQL وHTTPS/JWT وتوليد OpenAPI ونشر حزمة API. سبق ذلك تصحيح إعداد اختبار كان يختار فترة أقفلها اختبار سابق؛ الاختبار الحالي يختار فترة مفتوحة ويختبر رفض المغلقة صراحةً.

**الاستكمال التالي داخل GL02:** العملات الأجنبية والطباعة واستيراد الميزان، ثم بوابة المطابقة/القطع مع اعتماديات SAL03/PUR03 غير المنفذة. انتهت فجوتا تتبع روابط العكس وتعديل رأس المسودة. GL03 ما زال يحتاج أنواع القيود والمرفقات والعكس المجدول والطرف والمشروع. لا ادعاء اكتمال قسم GL.

### دفعة تتبع العكس السابقة — تحقق رفعها

رُفعت إلى main عند `0004eea0989c46d7d78ea2f0da7a9a465d5fa93f` ونجح GitHub Actions run `40329836077` بإجمالي **268/268** متضمنًا SQL وHTTPS/JWT. فشل تهيئة SQL محليًا المذكور أدناه عولج بالتحقق عن بعد للـcommit نفسه.


**الدفعة السابقة: استكمال تتبع عكس الافتتاح GL02، دون ترقية SQL (017).** يشمل OpeningRevision وOpeningModifiedAt الآن حجز مسودة العكس وإلغاءها وترحيلها على الأصل، مرة واحدة لكل أمر ناجح. أحداث تدقيق مستقلة على معرف الأصل تحفظ التغيير مع المصدر والعكس والإيصال والأثر في معاملة واحدة. الحراسة تسمح بزيادة الإصدار مع رابط العكس فقط ولا تتيح تعديل المحتوى المالي المرحّل.

أضيفت أربعة اختبارات تطبيق لدورة الحجز/الإلغاء/الترحيل وإعادة المفاتيح ووقت التعديل وفشل الحفظ عند كل مرحلة، واختبارات SQL لإعادة الحجز بالتزامن وفشل تدقيق الترحيل وإعادة المحاولة وحماية تعديل بيانات أصل مرحّل مستقلًا، وفحوص HTTPS لحقول الحجز والإلغاء. نجح البناء المحلي و267/267 دون SQL. تعذر بدء SQL Server 2022 في بيئة العمل الحالية؛ محاولة --sql وصلت إلى 267/268 وفشلت عند الاتصال قبل اختبارات SQL، وليست فشلًا مثبتًا في قواعد التطبيق. دليل هذه الدفعة المحفوظ هو التشغيل المحلي دون SQL؛ يجب مراجعة CI للدفعة نفسها قبل اعتماد نتائج SQL/HTTPS الحي.

**نقطة الاستكمال داخل GL02:** العملات الأجنبية والطباعة واستيراد الميزان وتعديل تواريخ الرأس، وبوابة المطابقة/القطع التي تعتمد على SAL03/PUR03 والمصادر غير المنفذة. انتهت فجوة عداد/وقت تعديل روابط العكس؛ لا تعِد تنفيذها. لا ادعاء اكتمال GL02 أو المطابقة الشاملة. المخططات الأصلية محفوظة دون تعديل. كل commit جديد يبدأ بـ temp ويرفع مباشرة إلى main وفق طلب المستخدم في 2026-10-26.

### دفعة GL02 السابقة — مرجع تاريخي

**آخر دفعة مكتملة داخل GL: دورة الافتتاح العام GL02 — المخطط 017.** نجحت **264/264** على SQL Server وHTTPS/JWT، والبناء دون أخطاء أو تحذيرات. المصدر GeneralOpening (4) على JournalEntry نفسه، ببيانات OperationStartsOn وPreviousTrialBalanceReference وOpeningCurrencyCode/ExchangeRate وعداد/وقت تعديل افتتاح. لا جدول أرصدة زائد فوق اليومية. إنشاء/بحث/تفاصيل/تعديل/معاينة تسوية تحت general-openings، واعتماد وترحيل وإعادة وإلغاء وعكس من واجهات اليومية الموجودة ومعالجة GL05/GL06.

الحسابات الرقابية ممنوعة من GL02 لتثبت من مصادرها؛ الرأس والسطور بالعملة الوظيفية وسعر 1 حاليًا. يُسمح بمسودة غير متزنة وتمنع عند التقديم؛ الاعتماد مستقل والأثر والتدقيق وOutbox والإيصال ذري. العكس يحفظ مصدر الأصل ومرجعه ويستخدم مبالغه التاريخية. معاينة التسوية تستخرج حسابات افتتاح المخزون التاريخية وتعرض الرصيد المرحل والتغيير المقترح والرصيد الناتج؛ لا تضيف القيد المرشح بعد ترحيله مرتين ولا تعلن اكتمال بقية الدفاتر. AllSubledgersImplemented=false.

ثمانية اختبارات تطبيق جديدة مع SQL لإنشاء متزامن/تعديل/اعتماد/ترحيل/عكس، وHTTPS للبيانات ونطاق المعاينة ودورة الحالات. ترقية 017 ذرية قابلة للإعادة تحفظ المصادر القديمة؛ حقن فشل DDL لا يترك أعمدة أو إصدارًا جزئيًا. تقرير التحقق وOpenAPI مطابقان لبصمة المصادر. ابحث عن commit بعنوان `Add general ledger opening source lifecycle and historical clearing preview`.

**نقطة الاستكمال داخل GL02:** بوابة تصفير ومطابقة افتتاح شاملة وسياسة القطع لم تنفذ؛ المعاينة معلوماتية ومسار الترحيل الحالي باستبعاد الرقابات. كذلك العملات الأجنبية والطباعة والاستيراد وتعديل تواريخ الرأس وتوحيد العداد/آخر تعديل مع تغييرات روابط العكس. لا تنتقل لقسم آخر؛ أكمل GL02 المستقل ثم ترتيب GL14/GL03 وبقية [خطة القسم](gl-completion.md)، وسجل اعتماديات الذمم غير المنفذة. [العقد الكامل والحدود](general-openings.md). الملفات JournalService.GeneralOpening.cs وعقود GeneralOpening وGeneralOpeningsEndpoints.cs وترقية 017؛ لا تغير التحليل الأصلي.

**آخر رفع كود GL02 متحقق:** `197d6cfe62d8fef4077c0e8ecdd02f69090f1905` إلى main بعد نجاح 264/264، وتطابق SHA عبر ls-remote. يضم ترقية 017 ودورة المصدر والمعاينة والعكس. المستودع private. نجاح CI لهذه الدفعة لم يتحقق بعد؛ لا تستنتجه من الاختبارات المحلية.

**رفع GitHub متحقق في هذه الجلسة:** الفرع main عند e643c1f0b46709b0d5115f8e3d35107690d79462، مطابق للرأس المحلي قبل بدء GL02. تحقق عبر ls-remote، والمستودع ameenalqershi/ameen-erp بخصوصية private وفق GitHub. شمل الرفع دفعات العكس وتقارير المخزون ومعالجة القيود GL05/GL06. لا تستنتج منه رفع تغييرات GL02 اللاحقة.

### دفعة معالجة القيود السابقة (تاريخية)

**آخر دفعة مكتملة: مراجعة ومعالجة القيود جماعيًا GL05/GL06.** نجحت **256/256** على SQL Server وHTTPS/JWT، والبناء Release دون أخطاء أو تحذيرات. المخطط 016 دون ترقية. أضيفت أربعة مسارات: processing-queue وprocessing-preview وprocess-batch وprocessing-history ضمن journals. قائمة حسب الفترة والفرع والحالة والمصدر ونطاق التسلسل ومعاينة مانع كل قيد. دفعة اعتماد/إعادة/ترحيل بمعاملة مستقلة لكل قيد ونتيجة صريحة للنجاح والفشل.

المفاتيح داخل عناصر الدفعة وليست رأس Idempotency-Key عام؛ 1–100 عنصر، لا تكرار للقيد/المفتاح. الرفض قبل التعديل يحفظ تدقيقًا وإيصال Rejected دون تغيير القيد. إعادة مفتاح مرفوض تعيد القرار السابق؛ للتصحيح استخدم إصدارًا حاليًا ومفتاحًا جديدًا. فشل التخزين المعروف يعيد RejectionRecorded=false ولا يستهلك المفتاح وتتابع الدفعة العناصر الأخرى. الأخطاء التقنية غير المصنفة أو انقطاع الطلب قد يوقفان الدفعة بعد التزام عناصر سابقة؛ إعادة المفاتيح نفسها تستأنف بأمان. لا ادعاء معاملة واحدة أو نجاح كامل.

أضيف تفريغ ChangeTracker داخل SqlTransactionRunner بعد قفل الشركة وقبل التفويض؛ اختبار SQL أثبت أن عضوية محملة قديمة لا تسمح بإعادة ترحيل بعد سحب الصلاحية خارجيًا. اختُبرت 6 إعادات متزامنة للدفعة، وفشل Outbox لقيد مع نجاح نظيره ثم retry دون تكرار، وسجل الرفض المستقل، ومعاينة HTTP ودفعة مختلطة وإعادتها. تسعة اختبارات تطبيق جديدة. تقرير التحقق وOpenAPI مطابقان لبصمة المصادر. ابحث عن commit بعنوان `Add journal review queues and independently transactional batch processing`. هذه الدفعة محلية؛ لا تستنتج رفع GitHub منها.

[عقد GL05/GL06 وحدوده](journal-processing.md). لا تزال مراحل الاعتماد والمراجع المستقل ورأس الإجراء بإصدار عددي وتوجيه بقية المصادر وعامل Outbox والسجل التقني الخارجي الدائم فجوات. **نقطة الاستكمال: GL02 وتسوية الافتتاح، ثم بقية خطة GL؛ لا تعُد إلى دورة استبدال المخزون أو تقاريره إلا لضرورة مثبتة داخل GL.** الملفات الجديدة JournalService.Processing.cs وعقود Contracts/JournalProcessing، والاختبارات JournalProcessingTests.cs وJournalProcessingSqlIntegration.cs. لا تعدل التحليل الأصلي.

### دفعة تقارير المخزون السابقة (تاريخية)

**آخر دفعة مكتملة: تقارير أرصدة وحركة المخزون RPT08/RPT09 — المخطط يبقى 016.** نجحت **247/247** على SQL Server وHTTPS/JWT، وبناء Release بصفر أخطاء وتحذيرات. أضيف GET reports/inventory-balances وGET reports/items/{itemId}/ledger، بعزل الشركة والفروع ومرشحات المخزن والمجموعة والصنف والدفعة/التسلسل بحسب التقرير، وصفحات وروابط مستند وقيد ووحدات المصدر التاريخية.

الرصيد السابق والحركة والختام بالقيم المسجلة والوحدة الأساسية، مع احتساب الرصيد الجاري عبر الصفحات وإجماليات النطاق كاملًا قبل pagination. لا تدخل المستندات غير المرحلة ولا تحذف البطاقات المعطلة تاريخها. ثبت أن العكس اللاحق لا يغير رصيد تاريخ سابق، وصحة صفحة العكس ووحداته التاريخية، ومطابقة قيمة المخزون مع حساب الرقابة بالنطاق المختبر. showZero=false يخفي الختام الصفري وتصبح الإجماليات للصفوف المتبقية. لا تجمع كميات أصناف مختلفة في إجمالي واحد.

ثمانية اختبارات تطبيق جديدة، مع فحوص SQL للمرشحات ومطابقة الرقابة وصفحات العكس، وفحوص HTTPS للردود وروابط المصدر وتمثيل Decimal. تقرير التحقق وOpenAPI مطابقان لبصمة المصادر. ابحث عن commit بعنوان `Add scoped inventory balance and item ledger reports with historical reversal coverage`. هذه الدفعة محلية؛ لا تستنتج رفع GitHub منها.

**حدود ونقطة الاستكمال:** InventoryOpeningsOnly=true؛ التقرير يعرض افتتاح المخزون وعكسه فقط. لا حجز/متاح/طريق/عزل أو محرك تكلفة أو مطابقة آلية عامة أو طباعة/تصدير. لا اختيار عملة أو تفصيل، وto حد الرصيد نفسه في RPT08. التجميع الحالي في الذاكرة يحتاج تحسينًا للأحجام الكبيرة. تفاصيل العقد في [تقارير المخزون](inventory-reports.md). النسبة **12.0% (330/2759)** لتغطية الحقول، و**30/150 شاشة جزئية، صفر مكتملة**؛ ليست نسبة جاهزية ERP.

التالي **GL02 وتسوية حساب الافتتاح**، ثم استبدال افتتاح مصحح مرتبط بعد العكس قبل تعميم مصادر المخزون ومحرك التكلفة. لا تعدل فهارس منع تكرار الافتتاح دون تصميم مصدر الاستبدال. ملفات هذه الدفعة ReportService.Inventory.cs وعقود Contracts/InventoryReport، ومساران في ReportsEndpoints.cs، واختبارات InventoryReportTests.cs مع توسيع SQL/HTTPS. لا تغير التحليل الأصلي.

### دفعة العكس السابقة (تاريخية)

**آخر دفعة مكتملة: عكس افتتاح المخزون من المصدر — المخطط 16.** نجحت **239/239** على SQL Server وHTTPS/JWT. أضيفت 4 عمليات API: GET/POST /{sourceId}/reversals وPOST /{reversalId}/correct-reversal وPOST /{reversalId}/post-reversal. تنشأ مسودة عكس مستقلة برقم IOR وسبب وتاريخ أثر في فترة مفتوحة، وتنسخ سطور الأصل وقيمه التاريخية دون إعادة احتساب. تمر بدورة تقديم/إعادة/تصحيح سبب/اعتماد منفصل؛ لا تعديل للسطور أو التاريخ بعد الإنشاء. إلغاء المسودة يتيح عكسًا آخر، والفهرس يمنع أكثر من عكس غير ملغى للمصدر.

الترحيل يتطلب inventory.opening.reverse وinventory.opening.post، مع إعادة فحص الصلاحية والمصدر والفترة. يحفظ حركات سالبة مرتبطة بـReversesMovementId وقيدًا تاريخيًا مقابلًا ورابطه في القيد الأصلي والتدقيق وOutbox والإيصال معًا. يبقى مستند الأصل Posted تاريخيًا، ويصبح قيده Reversed؛ قائمة reversals تعرض التصحيح المرتبط. لا يسمح بعكس العكس ولا عكس القيد وحده ولا تكرار الأثر بمفتاح آخر. فشل Outbox يعيد القيد الأصلي إلى Posted بلا رابط ويترك مستند العكس Approved بلا حركات جديدة.

ثبتت مطابقة كمية وقيمة الأصل والعكس إلى صفر، والترحيل بدور runtime، والإعادة المتزامنة للمسودة والترحيل، ومنع المصدر المزدوج، والحسابات التاريخية رغم تغيير إعداد التسوية، وعكس فترة مقفلة داخل فترة لاحقة مفتوحة. الإقفال يتحقق من ترابط الأصل والعكس والحركات والقيد عبر الفترتين. ترقية 016 ذرية وقابلة للإعادة ومراجعها مركبة بالشركة، وتسمح بالإشارة السالبة فقط مع مرجع عكس وتحافظ على منع تكرار الافتتاح الأصلي والتسلسل.

ابحث عن commit بعنوان `Reverse inventory openings through approved source documents and atomic ledgers`. [عقد العكس وحدوده](inventory-opening-reversals.md). التقرير وOpenAPI مطابقان لبصمة المصادر. هذه الدفعة محلية حتى يثبت رفعها؛ آخر رفع متحقق مسجل في السجل السابق أدناه.

**نقطة الاستكمال الدقيقة:** GL02 وتسوية حساب افتتاح يصفر/استبعاد الرقابات المثبتة تفصيليًا لم تنفذ. كذلك إعادة افتتاح مصحح بعد عكس الأصل ما زالت محظورة بفهرس الافتتاح التاريخي؛ يلزم تصميم مصدر استبدال مرتبط دون حذف التاريخ. قبل حركات صرف/استلام/تحويل: عمم المصدر والفهارس وأضف فحص المتاح والحركات اللاحقة عند عكس الافتتاح. العكس الحالي كامل فقط، ولا يتجاوز مصدر افتتاح بلا دورات حركة لاحقة منفذة. بقيت الطباعة والأجنبية والمواقع وسياسات القطع والجودة. لا تعد F12 الجزئي القديم فجوة عكس معلقة: حقل الحالة ودورة العكس أصبحا منفذين ضمن نطاق INV06 الحالي؛ بقية حقول الشاشة الجزئية موثقة.

### دفعة ترحيل INV06 السابقة (تاريخية)

**آخر دفعة مكتملة: ترحيل افتتاح المخزون INV06 — المخطط 15 (2026-10-24).** نجحت **232/232** على SQL وHTTPS/JWT؛ أضيف 11 اختبار تطبيق للترحيل وتوسعت اختبارات الترقية والتكامل. المستند Approved يرحل إلى Posted مع StockMovement مجمع للصنف/الهوية وقيد رقابة متزن مصدره InventoryOpening ورابط مصدر فريد وتدقيق وإيصال وحدثي Outbox داخل معاملة واحدة. لا يوجد حفظ جزئي عند فشل Outbox. اختُبرت الإعادة المتزامنة والمنع بمفتاح/مستند مختلف، والتسلسل عبر مخزنين، وسحب صلاحية الترحيل بعد نجاح الطلب.

أضيف GET/PUT posting-settings وصلاحية inventory.opening.post وPOST /{id}/post وGET stock-movements. إعداد الحساب المقابل صريح على مستوى الشركة بلا حساب افتراضي؛ اختيار هذا المستوى أولي موثق ولا يمثل اعتماد سياسة نهائية من المستخدم. المعاينة تعرض الحسابين وقيمة القيد وإصداري الإعداد والمخزن؛ الترحيل يرفض تغيرهما. مدين رقابة المخزن ودائن حساب التسوية، بالعملة الوظيفية، لمخزن عادي دون مواقع. لا دعم لافتتاح إجماليه صفر أو هوية معزولة/مرفوضة/منتهية ضمن هذا النطاق.

حركات المخزون غير قابلة للتعديل؛ تقفل بنية الصنف والتحويلات والمخزن وأرقام/تواريخ الهوية بعد الحركة. GL07 يرفض عكس قيد المخزون منفردًا. إقفال الفترة يطابق المستند والحركات والقيد، واختُبر كشف قيمة مخزون فاسدة حتى مع اتزان القيد. اختُبر الترحيل بدور runtime ومنع تعديل الحركة منه. ترقية 015 ذرية وقابلة للإعادة، ولا تُنشئ إعدادات حساب أو أرصدة تلقائية. تطبيق runtime-role.sql بعد 015 مطلوب.

ابحث عن commit بعنوان `Post inventory openings atomically to stock and general ledger`. [العقد المحدث وحدود النطاق](inventory-openings.md). تقرير التحقق وOpenAPI مطابقان لبصمة الكود. نسبة الحقول تبقى 11.4% لأن هذه الدفعة تكمل سلوك الترحيل لحقول موجودة، وليست إضافة شكلية لحقول جديدة.

**نقطة الاستكمال الدقيقة:** عكس المصدر يجب أن يعكس المخزون والقيد معًا ويحافظ على القيم التاريخية؛ ثم تسوية GL02 عبر حساب افتتاح يصفر أو استبعاد الرقابات المثبتة تفصيليًا وفق التحليل. GL02 ليس منفذًا، ولا تدّع اكتمال عدم الازدواج بين دورتيه حتى اختبار التكامل. قبل حركات INV07/INV08 أو العكس: عمم نوع المصدر واتجاه الحركة وفهارس StockMovements الحالية الخاصة بالافتتاح فقط، دون إزالة منع تكرار الافتتاح/التسلسل. بقيت سياسة القطع والجودة والمواقع والأجنبية وإعادة المعتمد للتصحيح والطباعة. لا تغير التحليل الأصلي لتغطي الفجوات.

**آخر رفع كود متحقق:** 01149dc إلى main في المستودع الخاص، وتحقق تطابق SHA والخصوصية. يتضمن ترحيل افتتاح المخزون 015 وكل دفعات 013/014 وتعليمات العمل السريع. الاختبارات المحلية 232/232؛ كان CI لـ01149dc جاريًا وقت تسجيل هذه المعلومة، ولا يعد ناجحًا حتى التحقق من نتيجته.


### دفعة تجهيز INV06 السابقة (تاريخية)

**دفعة مكتملة: دورة تجهيز المخزون أول المدة INV06 — المخطط 14.** نجحت **221/221** على SQL Server وHTTPS/JWT (13 اختبارًا وظيفيًا جديدًا إضافة إلى توسيع اختبارات التكامل). أضيفت 10 عمليات API: إنشاء/تعديل/بحث/تفاصيل/معاينة قيمة/تدقيق/تقديم/إعادة/اعتماد/إلغاء. الملفات مستقلة: InventoryOpening وInventoryOpeningLine وInventoryOpeningState، وعقود مستقلة، وخدمة مقسمة أوامر واستعلامات، وEndpoints وتهيئة EF خاصان بالميزة.

تدعم الدفعة سطور الأصناف والوحدات والكميات والتكلفة، وتحويلًا دقيقًا ولقطة تاريخية للدفعة/التسلسل، وترقيمًا ذريًا حسب الشركة والسنة. الصلاحيات inventory.opening.write وinventory.opening.approve مستقلة عن القيود، مع منع اعتماد المنشئ وعزل الفروع. اختُبر التراجع عند فشل التدقيق وتبديل السطور بحساب runtime والتزامن وإعادة الطلب. قفل الفترة يفحص الافتتاحات غير الملغاة وتعرض معاينته PendingInventoryOpeningCount. وجود المستند يقفل تغيير عملة الشركة الوظيفية.

ابحث عن commit بعنوان `Add inventory opening preparation workflow with valuation and close safeguards`. [العقد وحدود التشغيل](inventory-openings.md). الدفعة محفوظة محليًا؛ لا تستنتج رفع GitHub من هذا السجل. يجب تطبيق 014 ثم runtime-role.sql بعد 013. التقرير وOpenAPI مطابقان لبصمة المصادر.

**نقطة الاستكمال في 014 (استكمل الترحيل في 015 أعلاه):** لم يكن يوجد ترحيل أو STOCK_MOVEMENT أو قيد رقابة أو عكس/طباعة. CanPost=false دائمًا. المخطط الحالي يتوقف عند Approved، والفترات التي تضم افتتاحًا غير ملغى تمنع من الإقفال. التالي تنفيذ دفتر الكميات/القيمة ومصدر افتتاح آلي للأستاذ مع تسوية GL02 لمنع تكرار الافتتاح، ثم الترحيل الذري والعكس واختبارات السباق. قبل ذلك احسم سياسة حساب الرقابة/حساب المقابل والتكلفة وتاريخ القطع والجودة والصلاحية؛ لا تمكّن ترحيلًا شكليًا. الأجنبية ومواقع المخزن وإدخال التسلسل النصي فجوات موثقة. التحليل الأصلي لم يتغير.

### دفعة INV05 السابقة

**آخر دفعة مكتملة: فصل رمز INV05 عن رقم الهوية — المخطط 13.** نجحت **208/208** على SQL وHTTPS/JWT. أضيف LotNumber/SerialNumber مستقلاً عن Code في الدومين والعقود والبحث والتدقيق، مع تفرد الرقم حتى بعد التعطيل، ورفض رقم النوع المقابل. التعديل الذي يغفل الرقم يحافظ عليه؛ الإنشاء القديم يغفله فيستخدم Code، مع إبقاء بصمة طلباته دون تغيير. ترقية 013 تملأ الأرقام القديمة دون تغيير معرفات/إصدارات السجلات؛ ثبت تراجعها عند فشل DDL وتكرارها بأمان على بيانات فعلية. التقرير وOpenAPI مطابقان لبصمة الكود.

ابحث عن commit بعنوان `Separate inventory card codes from lot and serial numbers`. لا تعِد فصل الأرقام؛ المهمة مكتملة في نطاق التوافق الموثق. [العقد](inventory-identities.md). F03 صار منفذًا؛ F09/F10 جزئيان بسبب دعم إغفال الرقم للعميل القديم. النشر يتطلب تطبيق 013 مع وقف الكتابة أثناء استبدال المضيف القديم.

**التالي:** اعتماد سياسة الجودة والصلاحية وتحديد عقد يوجب الرقم صراحة، ثم عرض الاستخدام وربط INV06/INV07 مع دفتر المخزون وقفل الهوية المستخدمة. لا يوجد دفتر كميات/تكلفة أو ضمان بعد. نتائج الدفعات أدناه تاريخية؛ هذه الفقرة والمصفوفات الآلية أحدث مرجع. التحليل الأصلي لم يتغير.

### دفعة الأساس السابقة

**آخر دفعة مكتملة: أساس بطاقة INV05 — 2026-10-22.** نجحت **205/205** على SQL Server وHTTPS/JWT، مع ترقية قاعدة البيانات إلى **12**. أضيفت 12 عملية API للدفعات والتسلسل: إنشاء وتعديل وبحث وتفاصيل وحالة وتدقيق، في ملفات مستقلة. حُمي تتبع الصنف بعد وجود هوية له، وثبت التفرد تحت التزامن والتراجع عند فشل التدقيق. التقرير وOpenAPI مطابقان لبصمة المصادر. تحقق رفع الدفعة السابقة `a4df1d5` وبقاء المستودع خاصًا.

ابحث عن commit بعنوان `Add inventory lot and serial identity foundation with SQL and API coverage`. [العقد والفجوات](inventory-identities.md). شاشة INV05 جزئية: اكتمل فصل الرمز في الدفعة الأحدث؛ تبقى قائمة الجودة وشرط الصلاحية وعرض الاستخدام وقفل هوية مستخدمة قبل حركة المخزون. لا يوجد دفتر كميات أو تكلفة أو ضمان؛ لا تعِد إنشاء بطاقة INV05 من الصفر. النسب الآلية أدناه تقيس تغطية الحقول فقط.

مهمة الفصل المحددة في هذه الدفعة السابقة أُنجزت في المخطط 13 أعلاه؛ بقية الاعتماديات ما زالت مفتوحة.

**آخر دفعة مكتملة: استكمال بحث SYS14.** رُفعت دفعتا المعمارية `5f09477` و`2f9955c` إلى origin/main وتحقق تطابق SHA والخصوصية، ونجح CI لـ`2f9955c`. أضيفت مرشحات UTC والمستخدم التاريخي والحدث والمرجع وتفاصيل الحدث في AuditService وSqlErpStore.Audit، مع مقارنة SQL ثنائية تحترم حالة الأحرف والمسافات. نجحت **200/200** على SQL وHTTPS/JWT؛ لا ترقية SQL (الإصدار 11).

ابحث عن commit بعنوان `Complete SYS14 audit search filters and automate progress percentages`. نُقل F09/F10 من implemented إلى partial لتصحيح الفرق بين المفتاح التاريخي ومرجع السياسة المعتمد، وأضيف F03/F04 كجزئيين لأن التاريخ اختياري للتوافق. النسب الآلية أدناه هي المرجع: انخفاض المنفذ من 283 إلى 281 تصحيح تصنيف، وليس تراجعًا في الوظائف. لم تعد نسبة الحقول رقمًا يدويًا. [العقد وحدود التنفيذ](audit-log.md).

الخطوة التالية: متابعة دفتر المخزون والمستندات والدفعات وفق الاعتماديات السابقة، أو استكمال مرشحات SYS14 للشاشة/رقم المستند بعد تعريف ربط مؤرخ للمصادر. لا تُضف تصديرًا أو تدقيق قراءة دون صلاحيات وسياسة ملائمة. لا مهمة نصف منفذة متروكة في هذه الدفعة.

### دفعة المعمارية السابقة


**دفعة مكتملة سابقة: فصل خدمات التطبيق في DI.** أزيل ErpService من الإنتاج لصالح 18 خدمة مستقلة Scoped، وApplicationService قاعدة مجردة للمعاملة والتفويض والتدقيق والصفحات فقط. كل Endpoint يستقبل خدمته مباشرة، والاعتماديات بين الميزات معلنة في constructors بلا Service Locator. نجحت **194/194** على SQL/HTTPS/JWT، بما فيها ServiceCompositionTests لمشاركة الحالة والتراجع ونطاقات DI. OpenAPI مطابق تمامًا، وSQL المولد مطابق بايتًا ببايت؛ الإصدار 11 والتغطية الوظيفية ثابتان.

ابحث عن commit بعنوان `Replace application facade with scoped feature services and explicit dependencies`. اختبار TestServices يجمع خصائص الخدمات الحقيقية داخل مشروع الاختبار فقط، ولا يعود facade إنتاجيًا. استكمل وظائف النظام وفق المخطط بعد هذه الدفعة، مستخدمًا خدمات Features المستقلة والتسجيل في ApplicationServiceRegistration. لا توجد مهمة فصل DI غير مكتملة. رُفعت هذه الدفعة لاحقًا إلى GitHub وتحقق نجاح CI كما في السجل الأحدث أعلاه.


**أولوية المستخدم الحالية: إعادة تنظيم المعمارية قبل إضافة وظائف — مكتملة.** فصلت مسارات API إلى 17 مجموعة وملف تسجيل قصير، وكل نوع Domain وعقد إلى ملف مستقل، وخدمات التطبيق إلى Features مع Common وAbstractions، وتهيئة EF وحراسة الحفظ إلى Persistence. نجحت **193/193** على SQL/HTTPS/JWT. مخطط SQL المولد مطابق قبل/بعد النقل، وOpenAPI مطابق عدا ترتيب قائمة tags العامة. بقي مخطط البيانات 11 والتغطية الوظيفية دون تغيير.

آخر دفعة كود: ابحث عن commit بعنوان `Organize backend by feature and separate domain contracts endpoints and persistence`. لم تعد ملفات Domain الجامعة ولا Contracts.cs ولا CommandService.cs/QueryService.cs موجودة؛ لا تعِد إنشاءها. اقرأ [خريطة التنظيم](code-organization.md) قبل الاستكمال. كانت تلك الدفعة تبقي ErpService واجهة partial؛ الدفعة الأحدث أزالتها لصالح الخدمات المستقلة. نتيجة هذه الدفعة محلية؛ لا يُستنتج رفعها من نجاح رفع الدفعة السابقة.


**توجيه المستخدم الأحدث:** رفع المشروع إلى GitHub أولًا ثم استئناف الباك إند. تحقق الاتصال بحساب `ameenalqershi` وأنشئ المستودع [ameen-erp](https://github.com/ameenalqershi/ameen-erp) بخصوصية Private. نجح رفع دفعة INV01 وتحقق تطابق main عند `30de848` مع النسخة المحلية وبقاء المستودع Private؛ لا تحفظ رمز الوصول في Git. أولوية الاستكمال بعد نجاح الرفع هي INV01 وفق العقد والفجوات أدناه.

- دفعات الأساس السابقة: `8092be2` لمجموعات INV02، و`0ad7b55` لبطاقة INV04، و`ab62409` لعرض استخدام المجموعات وسجل التسليم. إصدار قاعدة البيانات المستهدف في الدفعة الحالية 11؛ لا تعدّل السكربتات المرقمة المعتمدة.
- اكتملت دفعة INV01 الحالية بعد دفعة عرض استخدام مجموعة الأصناف INV02، وتحدث السجل من نتائج التحقق الفعلية. لا تعِد تنفيذ هذه المهمة؛ افحص Git والكود الحالي أولًا.
- حالة التنفيذ النهائية لهذه الدفعة وتفاصيل الاستئناف مسجلة أدناه. مصدر الحقيقة للكود هو HEAD و`git status`، وللحقول المصفوفات المولدة، وللاختبارات التقرير ذو بصمة المصادر.
- المستودع المحلي `/root/ameen-erp`؛ المستودع البعيد الخاص `ameenalqershi/ameen-erp` أنشئ في هذه الجلسة. المصادقة المستخدمة مؤقتة خارج المشروع؛ تحذف ملفاتها عند التسليم، فلا تفترض وجود جلسة مصادقة دائمة.

## اقرأ بهذا الترتيب

1. هذا السجل، ثم [خطة الاعتماديات](completion-plan.md) و[حدود المطابقة](traceability.md).
2. [التحليل الأصلي](../../muhaseb-erp-analysis/README.md)، وملف القسم في `muhaseb-erp-analysis/docs/modules/`، وحقوله في `data/fields.csv` أو `data/screens.json`.
3. المخطط المقابل من `diagrams/source/`، ثم `data-model.mmd` و`document-lifecycle.mmd` و`posting-sequence.mmd` و`period-close.mmd` عند إضافة مستند مؤثر.
4. [الشاشات](screens-implementation.csv)، [الحقول الـ2759](fields-implementation.csv)، [المخططات](diagrams-implementation.csv)، [قواعد العمل](rules-implementation.csv). جدول الشاشات الكامل أدناه مشتق منها، وليس سجلًا منفصلًا يناقضها.
5. عقد الوحدة في `docs/backend/`، وخدمتها واختباراتها قبل كتابة الإضافة. [OpenAPI](../../backend/openapi/v1.json) مولد من المضيف، وليس بديلًا عن قواعد الأعمال.

حزمة التحليل الأصلية محمية ببصمات `manifest.json`. لا تغيّرها لتجعل التنفيذ يبدو مطابقًا. قوائم وسياسات لا يحددها التحليل توثق كفجوات أو استنتاجات محدودة، ولا تُعلن معتمدة. المطابقة مع إصدار محدد من المتكامل غير مثبتة في [سجل المرجع الأصلي](../../muhaseb-erp-analysis/docs/06-reference-parity.md).

## خريطة الكود وطريقة العمل

راجع [دليل تنظيم الملفات](code-organization.md) للمسارات الجديدة وقواعد إضافة الوظائف. أسماء الأنواع وnamespaces وعقود HTTP بقيت ثابتة.

| المسار | المسؤولية ونقطة التوسعة |
|---|---|
| `backend/src/Ameen.Erp.Domain/` | ملف لكل كيان/نوع في Common وOrganization وAccounting/Recurring وInventory وOperations |
| `backend/src/Ameen.Erp.Application/` | `Features/<Feature>` لخدمات مستقلة Scoped؛ Common/ApplicationService قاعدة مجردة للتنفيذ والتفويض؛ Abstractions للمنافذ؛ Contracts/<Feature> ملف لكل عقد |
| `backend/src/Ameen.Erp.Infrastructure/` | Persistence/Configurations لتهيئة الكيانات، ErpDbContext.Validation للحراسة، SqlErpStore وSqlTransactionRunner؛ الإصدار يطابق آخر ترقية |
| `backend/src/Ameen.Erp.Api/` | Endpoints/<Feature>Endpoints.cs للمسارات وEndpoints.cs للتسجيل فقط؛ ApiSupport والمضيف وOpenAPI منفصلة |
| `backend/database/` | ترقيات SQL مرتبة ذرية وقابلة للإعادة و`runtime-role.sql`؛ 011 أضيف للمخازن؛ التالي 012 إذا تطلب التغيير مخططًا جديدًا |
| `backend/tests/Ameen.Erp.Tests/` | مشغل اختبارات تنفيذي، MemoryStore، اختبارات تطبيق وSQL ومضيف HTTPS/JWT؛ سجّل الاختبارات الجديدة في `Program.cs` |
| `backend/scripts/traceability.py` | الربط الصريح للشاشات والحقول والمخططات والقواعد؛ عدّل المصدر ثم ولّد CSV/JSON |
| `backend/scripts/progress.py` | تحديث القسم الآلي من هذا السجل من المصفوفات وتقرير الاختبار؛ يدعم `--check` |
| `.github/workflows/` | إعداد CI؛ لا يعني وجوده أنه شُغّل عن بعد |

## سجل ترقيات البيانات المعتمدة

| الإصدار | الإضافة |
|---|---|
| 001 | النواة والشركات والفروع والفترات والحسابات والقيود والتدقيق والعضويات |
| 002 | القوالب الدورية وسجل التوليد وربط مصدر القيد |
| 003 | حقول بطاقة الحساب وطبيعة الرصيد وإصدارها |
| 004 | بطاقة العملة وإصدارها |
| 005 | شجرة المراكز ومسؤولها وحقول البطاقة |
| 006 | ملف الشركة القانوني والعنوان والتقويم والحالة |
| 007 | شجرة الفروع وعناوينها والمركز الافتراضي |
| 008 | دليل وحدات القياس |
| 009 | مجموعات الأصناف وروابط الحسابات |
| 010 | الأصناف ووحداتها الخاصة وباركودها وأسعارها |
| 011 | المخازن ومواقعها ومراجع الفرع والأمين والحساب |

كل ترقية مطبقة باختبارات إعادة التطبيق والتراجع المناسبة في `SchemaUpgradeIntegration.cs`. طبق سكربت دور التشغيل بعد الترقيات بحسب دليل التشغيل؛ تحرير وحدات الصنف يحتاج DELETE على ItemUnits. وجود جداول المخزون الأساسية لا يعني وجود STOCK_MOVEMENT.

## ثوابت لا تكسرها

- كل مرجع داخل الشركة نفسها، بمفاتيح SQL مركبة أيضًا. نطاق الفرع يعاد التحقق منه؛ العضوية الحالية مطلوبة حتى لإعادة طلب سابق. صلاحية administrator لإنشاء الشركة لا تمنح وصولًا تلقائيًا لكل الشركات.
- أوامر الكتابة عبر `Command` والمعاملة وقفل الشركة `sp_getapplock`. مفتاح إعادة الطلب مربوط بالشركة والمستخدم والعملية وبصمة الجسم؛ نفس المفتاح لا يصلح لجسم مختلف.
- `Version` GUID للتزامن؛ `Revision` عدد صحيح في البطاقات التي تنفذه؛ لا تعوّض أحدهما بالآخر في مطابقة الحقول.
- لا تعديل مالي لقيد مرحّل، ولا اعتماد من منشئه، ولا قيد يدوي لحساب رقابي. العكس بمستند مرتبط وبسعر الأصل المحفوظ. أسعار الصرف المستخدمة لا تتغير تاريخيًا.
- Decimal دقيق في JSON والتحويل؛ لا float ولا تقريب خفي. المبالغ تخضع لدقة العملة والكميات لدقة الوحدة. الوحدات البديلة ومعاملاتها خاصة بالصنف.
- Audit وOutbox والإيصال والأثر المالي تحفظ معًا؛ لا ترسل إلى خدمة خارجية داخل معاملة الترحيل. عامل تسليم Outbox لم ينفذ بعد.
- قبل تفعيل مخزون أو ذمم: أضف دفتره وقفل بنية المراجع المستخدمة ونسخ إعدادات المستند التاريخية والعكس وفحوص الإقفال. الإقفال الحالي للأستاذ فقط.
- الوحدات والمجموعات المستخدمة بصنف، حتى المعطل، مقفلة بنيويًا؛ الأصناف الفعالة تمنع تعطيل مراجعها. أسعار الصنف بالعملة الوظيفية، وتمنع تغييرها ما دامت الأسعار موجودة؛ التدقيق يحفظ العملة.
- جميع CRUD الحالية ليست إذنًا لتشغيل مستندات أو احتساب متاح بيع أو تكلفة أو ضريبة غير منفذة.

## المنفذ المتحقق منه قبل الدفعة الحالية

- تهيئة الشركة والفروع والعملات والتقويم والفترات وأسعار الصرف والمراكز والعضويات، مع قيود النطاق والحقول والبحث والتدقيق وفق المصفوفة.
- دليل الحسابات وشجرته وطبيعته وقفل بنية المستخدم، ودورة القيد اليدوي والإقفال الجزئي والعكس والتقارير الأولية.
- GL04: قوالب دورية وتوليد يدوي لمسودة واحدة لكل قالب وفترة وتقويم وسجل استخدام؛ لا عامل جدولة تلقائي.
- INV03: دليل وحدات ودقتها؛ INV02: شجرة مجموعات وحساباتها؛ INV04: أصناف ووحدات بديلة وباركود وأسعار وتحويلات. لا دفتر مخزون بعد.
- تحقق محلي حقيقي بـ.NET 10.0.402 وEF Core 10.0.13 وSQL Server 2025 Developer. أسماء الاختبارات والنتيجة والبصمة في [التقرير](verification-results.json)؛ القيود في [سجل التحقق](validation.md).

## فجوات وقرارات يجب حملها إلى الجلسة التالية

| الموضوع | الحالة والإجراء |
|---|---|
| COST01-F11 | نوع المركز غير محدد القيم، لم ينفذ؛ لا تختلق قائمة نهائية |
| SYS02-F09/F13 | المدير ونوع الموقع غير منفذين؛ مرجع المدير والقيم يحتاجان تحديدًا |
| SYS03 | تقويم فرعي وتعديل وإصدار عددي وإقفال شامل غير مكتملة |
| SYS05 | نوع السعر وتعطيله والإصدار العددي غير مكتملة |
| INV01 | بطاقة المخزن ومواقعه منفذة؛ Standard/Quarantine وMembership نطاق أولي موثق؛ الصلاحيات المستقلة للمخزن والاستخدام المستندي ومتـاح البيع غير منفذة |
| INV04 | Goods/Service/Material/Product وNone/Lot/Serial قوائم أولية مستنتجة؛ لا ضريبة ولا تتبع تشغيلي ولا استخدام مستندي. يجب قفل الوحدة الأساسية والتتبع بعد الحركة عند إضافة دفتر المخزون |
| INV02/INV04 والحسابات | يلزم تحديد أولوية حساب المخزن/المجموعة/الصنف قبل تمكين الترحيل المخزني؛ لا توريث أو تجاوز حسابات ضمني |
| الدولة والضريبة | لا نسب افتراضية ولا ادعاء اعتماد قانوني؛ ملفات الدولة تحتاج مصادر رسمية مؤرخة وقت التنفيذ |
| الإنتاج والهوية | جهة OIDC المستخدمة في الاختبار مؤقتة؛ بيانات جهة الإنتاج والشهادات والمراقبة والتعافي غير متحققة |
| أداء التقارير | تجميع بعض النتائج في التطبيق؛ لا اختبار حمل ولا snapshot مضمون بين استعلامات التقرير المتعددة |
| GitHub | المستودع الخاص أنشئ؛ سجل نتيجة التحقق من رفع كل دفعة قبل التسليم |

ترتيب العمل اللاحق: أكمل فجوات الأساس ذات السياسة المعروفة، ثم DOCUMENT/LINE_LINK/APPROVAL والأطراف والذمم والنقدية؛ أو أساس المخزون قبل حركاته وفق اعتماديات [الخطة](completion-plan.md). لا تمكّن دورة شراء أو بيع قبل اكتمال الذمم والمخزون والضريبة اللازمة. جدول كل شاشة أدناه يحدد ما بقي بدل عبارات عامة مثل «أنجز المحاسبة».

## استئناف الاختبارات والتسليم

```bash
cd /root/ameen-erp
git status --short
git log -5 --oneline
python3 backend/scripts/traceability.py --check
python3 backend/scripts/progress.py --check
python3 backend/scripts/verify.py --dotnet /root/.dotnet10/dotnet
# مع اتصال خادم اختبار معزول في AMEEN_TEST_SQL_CONNECTION:
python3 backend/scripts/verify.py --sql --dotnet /root/.dotnet10/dotnet
```

المسار المطلق لـSDK خاص بهذه البيئة؛ في غيرها استخدم `dotnet` وفق `backend/global.json`. `dotnet test` وحده لا يشغل مشغل هذا المشروع. دون `--sql` تتجاوز فحوص SQL والمضيف الفعلي صراحة؛ لا تسجلها ناجحة. الاختبارات الحية تنشئ قاعدة عشوائية وتحذف قاعدتها فقط، وتتطلب صلاحيات إعداد اختبار. لا تستخدم قاعدة إنتاج ولا تحفظ اتصالًا أو كلمة مرور في Git. سكربت `/tmp/start-ameen-sql.py` إن وُجد أداة جلسة مؤقتة، وليس متطلبًا للمستودع.

بعد نجاح الدفعة: طابق `sourceSha256` في التقرير مع المصادر الحالية، انسخ `backend/.artifacts/verification.json` إلى `docs/backend/verification-results.json` و`openapi-v1.json` إلى `backend/openapi/v1.json`، وحدّث `validation.md` والعقد. ولّد المصفوفات ثم السجل وشغّل فحصهما و`git diff --check`، ثم احفظ commit محليًا. لا تعدّل السكربتات المرقمة القديمة؛ أضف ترقية واختبار تراجع وإعادة تطبيق عند الحاجة. أوقف فقط خادم الاختبار الذي أنشأته واحذف بيانات اتصاله المؤقتة.

## سجل الدفعة الحالية ونقطة التوقف النهائية

**اكتملت دفعة INV01 بعد نجاح الرفع:** بطاقة المخزن ومواقعه بترقية 011، مع ضبط نطاق الفرع والتحقق من الأمين وحساب الأصل الرقابي، وحماية بنية مراجع الحساب والفرع، وتعديل المواقع والحالة والتدقيق وإعادة الطلب. Standard/Quarantine وMembership نطاق أولي موثق وغير محسوم من المستخدم. نجحت **193/193** على Release وSQL وHTTPS/JWT، وطابقت بصمة المصادر التقرير المحفوظ. لا مهمة كود نصف منفذة في هذه الدفعة؛ ابحث عن commit بعنوان `Implement INV01 warehouse cards and locations with protected references`. لا حركات مخزون. سجل القيود والتفاصيل في [warehouses.md](warehouses.md).

تحقق GitHub عند تسليم دفعة `30de848`: تطابق SHA البعيد والمحلي والخصوصية Private؛ CI للدفعة كان قيد التشغيل وقت الفحص، وCI للدفعة السابقة `011be0a` نجح. لا تُحوّل حالة قيد التشغيل إلى ادعاء نجاح.

### الدفعة السابقة: استخدام مجموعات الأصناف


اكتملت الدفعة التالية بعد `0ad7b55`؛ ابحث في Git عن commit بعنوان `Document backend handoff and complete item group usage queries`. لا توجد مهمة كود نصف منفذة متروكة ضمن هذه الدفعة.

- أضيف `GetItemGroupUsage` و`ListItemGroupItems` في `ItemGroupService.cs` ومسارا `/item-groups/{id}/usage` و`/item-groups/{id}/items` وعقد `ItemGroupUsage`.
- وحّد اجتياز الشجرة بين ملخص الاستخدام وحارس تعديل البنية. تدخل المراجع المعطلة في القفل، والعد المباشر منفصل عن الشجرة. القراءة تتطلب `ledger.read` على جميع الفروع؛ المعاينة لا تحجز صلاحية التعديل.
- أضيفت ستة اختبارات في `ItemGroupUsageTests.cs`، وامتد `ItemSqlIntegration.cs` و`HostedApiIntegration.cs`. نجحت الحزمة كاملة **183/183** في Release دون أخطاء أو تحذيرات، مع SQL حي ومضيف HTTPS/JWT. ثبتت بصمة المصادر قبل نسخ التقرير والعقد.
- لا ترقية SQL لهذه الدفعة؛ SchemaVersion يبقى 10. لا مخزون فعلي ولا ضرائب ولا واجهة أمامية.
- صحح وصف `module-INV` و`system-map` و`data-model` وملاحظات INV02/INV03 القديمة؛ السجلات الآلية أدناه تعكس ذلك.
- `progress.py --check` يكشف تقادم الجداول عن المصفوفات والتقرير المسجل؛ أضيف فحصه إلى CI. لا يتحقق هذا السكربت من صحة كل ادعاء يدوي، لذلك حدث هذا القسم بنفسك في كل دفعة.

**نقطة الاستئناف بعد INV01:** تأسيس دفتر المخزون والمستندات قبل تمكين INV06–INV10. اقرأ `data-model.mmd` و`posting-sequence.mmd` و`stock-transfer.mmd` وحقول المستندات: يجب تصميم DOCUMENT/DOCUMENT_LINE ونسخ الوحدة والمعامل والتكلفة وروابط الكميات وحالات الاعتماد والعكس، وحسم أولوية حساب المخزن/المجموعة. المخزن المعزول لا يدخل متاح البيع؛ اختبر ذلك عند إضافة الرصيد والحجز. لا تدّع أن تصنيف Quarantine وحده ينفذ القاعدة. يلزم أيضًا INV05 للدفعات والتسلسل والصلاحية وحسم حالات الجودة قبل إدخال مخزون متتبع. الترقية التالية 012؛ يجب قفل بنية الصنف والمخزن والمواقع المستخدمة وحفظ هويات المواقع بدل استبدالها الحالي عند أول مرجع تشغيلي.

نسب التغطية تحسب آليًا في القسم التالي من حالات الحقول والشاشات. قد تنخفض النسبة عند تصحيح ربط مبالغ فيه؛ ذلك تصحيح للمصفوفة لا حذف لكود منفذ. لا تكتب نسبة ثابتة مستقلة عنها.

قبل أي استكمال: نفّذ فحص المصفوفات والسجل، واقرأ حالة Git. بعد دفعتك حدّث هذا القسم والنتيجة والمصفوفات؛ لا تترك عبارة «قيد التنفيذ» إذا سلمت دفعة مكتملة.

## GL14-02 — isolated rejected-edit audit regression (AGENT-03, 2 November 2026)

On branch `temp/gl14-agent03-rejected-edit-atomicity-20261102` from main `d02dbb0140a4c2e5cdf619d363c2ca9121d2743f`, a non-authorizing GL14 domain regression verifies that invalid number series or approval route references and regressed timestamps reject edits and disable without altering code, origin, references, name, attachment requirement, creator, audit timestamps, revision or optimistic version. A valid edit updates permitted fields and advances audit exactly once. Local .NET 10 Release and HTTP/OpenAPI test host: **366/366 passed, 0 failed**; traceability and progress checks succeeded. On head `d709d42920a9ed75cb1ef77e08e422f16265baa7`, [branch CI #41538563967](https://github.com/ameenalqershi/ameen-erp/actions/runs/41538563967) and [PR CI #41538585623](https://github.com/ameenalqershi/ameen-erp/actions/runs/41538585623) both **SUCCESS** including isolated SQL Server, Release, HTTP/JWT and OpenAPI. [PR #17](https://github.com/ameenalqershi/ameen-erp/pull/17) is unmerged; its final documentation SHA requires fresh CI. No original analysis/schema, SYS07/SYS09 guard, approved document-kind mapping, GL14 API, production writes or financial policy changed. **GL14-02 remains PARTIAL, 0 new completed Notion tasks.**

<!-- generated-progress:start -->

## ملخص آلي من الأدلة المسجلة

الشاشات: 150؛ منفذة جزئيًا: 33؛ لم تبدأ: 117؛ مكتملة: 0. الحقول: 2759؛ المخططات: 28؛ القواعد: 20. لا يمثل ذلك نسبة اكتمال مالي أو تشغيلي.

آخر تقرير محفوظ: `2026-11-02T19:37:11.777004+00:00`؛ النتيجة `passed`؛ 356/356، فشل 0؛ SQL حي مطلوب: `True`. بصمة المصادر: `c13d8b407ef2a454df29b79d1953d501f10d880091dcfe4a161a628421700e18`. هذه نتيجة التقرير المحفوظ وليست فحصًا جديدًا للشجرة الحالية.

تغطية الحقول المنفذة/المشتقة/التدقيق: **13.8%** (382/2759)؛ الحقول الجزئية: 79. نسبة الشاشات التي بدأ تنفيذها: **22.0%**. هذه مؤشرات ربط متطلبات وليست نسبة جاهزية تشغيلية أو ساعات إنجاز.

### حصيلة الأقسام

| القسم | الشاشات | جزئي | لم يبدأ |
| --- | --- | --- | --- |
| SYS | 17 | 11 | 6 |
| GL | 14 | 11 | 3 |
| CASH | 11 | 0 | 11 |
| INV | 16 | 6 | 10 |
| SAL | 15 | 0 | 15 |
| PUR | 13 | 0 | 13 |
| POS | 6 | 0 | 6 |
| IMP | 6 | 0 | 6 |
| FA | 6 | 0 | 6 |
| COST | 5 | 1 | 4 |
| HR | 7 | 0 | 7 |
| MFG | 6 | 0 | 6 |
| PRJ | 6 | 0 | 6 |
| TAX | 8 | 0 | 8 |
| RPT | 14 | 4 | 10 |

### الحقول حسب حالة الربط

| الحالة | العدد |
| --- | --- |
| audit | 49 |
| derived | 5 |
| implemented | 328 |
| partial | 79 |
| pending | 2298 |

implemented يعني ربط الحقل بعقد؛ audit يعني وجوده في التدقيق؛ derived مشتق؛ partial جزئي؛ pending غير منفذ. راجع fields-implementation.csv لمعرفة اسم كل حقل وعقده.

### شاشات SYS

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| SYS01 | الشركات | partial | بطاقة قانونية وعنوانية وإصدار عددي؛ تحرير وحالة واستخدام وتدقيق؛ قفل العملة بعد الأثر وربط بداية أول سنة؛ منع كتابة الشركة المعطلة أو ناقصة البيانات | ملفات الدولة وشروط إلزام التسجيل الضريبي والتجاري والعنوان الوطني حسب الاختصاص؛ مرجع دول معتمد بدل تحقق شكل الرمز؛ مشروع تحويل العملة الوظيفية التاريخي |
| SYS02 | الفروع والإدارات | partial | بطاقة فرع وعنوان وشجرة بلا دورات؛ بحث ضمن الصلاحية دون توريث الفروع؛ قفل بنية المستخدم؛ مركز افتراضي متسق؛ تعديل وحالة واستخدام وتدقيق | نوع الموقع وقيمه ومرجع المدير بانتظار حسم التحليل؛ المركز الافتراضي إعداد لا يملأ سطور القيود تلقائيًا؛ الشركات القديمة تستكمل عناوين فروعها قبل التشغيل |
| SYS03 | السنوات والفترات المالية | partial | تقويم شهري افتراضي أو فترات صريحة تغطي السنة بلا فجوات أو تداخل؛ حالة إقفال الأستاذ مشتقة؛ بحث واستخدام وتدقيق؛ حماية التزامن والترحيل | تعديل تقويم قائم وإصدار عددي وإقفال السنة الشامل غير منفذ؛ لا يوجد تقويم مختلف لكل فرع؛ الفترة ذات أكثر من موعد دوري ترفض التوليد؛ لا حذف أو تعطيل بديل للإقفال |
| SYS04 | العملات | partial | بطاقة عملة بأسماء ورمز عرض واسم جزء وإصدار عددي؛ تحرير وحالة وبحث وتدقيق؛ قفل الدقة بعد الاستخدام وفحص التعطيل عند الترحيل | الرمز الداخلي موحد مع رمز العملة ولا يربط بقائمة ISO خارجية وغير قابل لإعادة التسمية؛ أسماء العملة الوظيفية والبطاقات القديمة تبدأ برمز ISO وتحتاج تحريرًا؛ تحويل الدقة التاريخية غير متاح |
| SYS05 | أسعار الصرف | partial | سعر موجب مؤرخ غير قابل للتعديل؛ بحث بالتاريخ والعملة والمصدر وصفحات؛ سعر نافذ باتجاه صريح؛ استخدام وتدقيق؛ اختيار مشترك مع التوليد الدوري | نوع السعر وقيمه ونطاق الفرع وحالة تعطيل السعر وإصداره العددي غير منفذة؛ لا تحويل تاريخي ولا إعادة تقييم أرصدة؛ المعاينة ليست حجزًا للسعر |
| SYS06 | المستخدمون | partial | تعطيل وتفعيل عضوية ERP؛ ربط sub بجهة OIDC | شاشة المستخدم الكاملة؛ إنشاء الهوية والبريد واللغة وMFA لدى مزود الهوية لم يُدمج تشغيليًا |
| SYS07 | الأدوار والصلاحيات | partial | منح عمليات خادمية صريحة لمستخدم داخل شركة؛ منع تغيير المستخدم لصلاحياته؛ أساس كتالوج أدوار SYS07 وقواعد السماح/المنع وحد المبلغ/العملة وFK إلى SYS09؛ ربط مستخدم-دور SQL 030 ومقيّم مستقل غير مفعل؛ استبدال قواعد الدور ذريًا مع فحص إصدار التعديل؛ رفض لقطات الأدوار الناقصة أو العابرة للشركات وتكرار الروابط قبل تقييم المنح؛ رفض عناصر قواعد الدور الفارغة دون تغيير الدور (مجال فقط)؛ رفض اللقطات الداخلية ذات قواعد مكررة أو عابرة للشركات في تقييم الدور المباشر والمسنَد (مجال فقط)؛ رفض القواعد المحمّلة برموز غير قياسية أو سقف/عملة غير متسق وصفوف اللقطات الفارغة (مجال فقط) | إدارة الأدوار وربطها بالعضويات عبر API وإنفاذ المنع الصريح ونطاق الفرع والمبلغ والعملة والعمليات المنفصلة والتدقيق وقبول API؛ كتابة الأدوار والروابط محجوبة |
| SYS08 | نطاقات بيانات المستخدم | partial | نطاق الشركة والفروع مطبق على الأوامر والتقارير؛ ربط عضوية-دور محمي SQL 030 دون إنفاذ | نطاق الحساب والمخزن والصندوق والعملاء ومراكز التكلفة المستقل؛ تفعيل إدارة الدور وربطه بالمستخدم |
| SYS09 | مسارات الاعتماد | partial | SYS09 dependency foundation: tenant-scoped route and stage schema, amount decimal(20,6), optional branch, currency FK, audit revision, stage uniqueness and GL14 route FK; domain-only conservative branch/amount overlap preflight with hydrated stage/audit integrity and cross-route stage identity uniqueness, and atomic stage replacement with revision; all unvalidated writes blocked | SYS07 role FK exists but membership/active/scope/deny enforcement is absent; document type allowed list, transactional overlap/scope arbitration, cross-currency policy, stage execution, notifications, authorization, endpoints and acceptance not implemented |
| SYS10 | ترقيم المستندات | partial | سلاسل عامة أو لفرع وسنة ونوع مستند وبادئة ورقم تال قابل للإعداد؛ حجز ذري موثق لا يعاد بعد الإلغاء؛ ربط اليومية والافتتاحات؛ تعديل وتعطيل وبحث واستخدام وإصدار وتدقيق مع حماية السجلات التاريخية | أنواع التشغيل الحالية Journal وInventoryOpening؛ الترقيم الضريبي مرفوض حتى TAX05 وملف الدولة؛ باقي أنواع المستندات تربط عند تنفيذ مصادرها ولا تدعى جاهزيتها |
| SYS11 | الخيارات العامة والربط المحاسبي | not-started | — | الشاشة كاملة |
| SYS12 | قوالب الطباعة | not-started | — | الشاشة كاملة |
| SYS13 | استيراد البيانات والأرصدة | not-started | — | الشاشة كاملة |
| SYS14 | سجل التدقيق | partial | قراءة سجل أحداث غير قابل للتعديل وتفاصيل الحدث؛ مرشحات UTC والمستخدم التاريخي ونوع الحدث والمرجع وصفحات؛ مقارنة هوية وحدث حساسة للحالة | مرشح الشاشة ورقم المستند ونطاق الفروع المستقل والتصدير وتدقيق القراءة غير منفذة؛ التاريخ اختياري للتوافق؛ المستخدم هو OIDC sub التاريخي ونوع الحدث مفتاح محفوظ دون كتالوج شاشة معتمد |
| SYS15 | النسخ والاستعادة | not-started | — | الشاشة كاملة |
| SYS16 | شروط وطرق الدفع | not-started | — | الشاشة كاملة |
| SYS17 | صندوق الموافقات والتنبيهات | not-started | — | الشاشة كاملة |

### شاشات GL

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| GL01 | دليل الحسابات | partial | بطاقة ثنائية اللغة وملاحظات وطبيعة مدينة/دائنة صريحة؛ تعديل وشجرة بلا دورات وقفل بنية المستخدم وسجل استخدام وتغييرات | مسار تحويل معتمد لتغيير بنية الحساب المستخدم؛ سجل الاستخدام الحالي للأستاذ والقوالب فقط حتى تنفيذ الدفاتر المساعدة |
| GL02 | الأرصدة الافتتاحية العامة | partial | افتتاح عام مصدره GeneralOpening ومرجع ميزان وتاريخ تشغيل وعملة وظيفية أو أجنبية بسعر تاريخي مثبت؛ قيد متزن ودورة مستقلة واعتماد وترحيل وعكس تاريخي؛ منع الحسابات الرقابية؛ معاينة حسابات التسوية التاريخية للمخزون دون عد القيد مرتين؛ إصدار ووقت تعديل يشملان حجز العكس وإلغاءه وترحيله؛ استبدال رأس المسودة والسطور مع نقل الفرع والفترة وترقيم السنة وإعادة التحقق؛ معاينة الأثر التفصيلية للمدين والدائن وسعر السطر والفرق دون تسجيل حركة أو تغيير القيد | الطباعة والاستيراد؛ تصفير حساب التسوية بوابة شاملة غير منفذ؛ الذمم والمصادر غير المنفذة ليست مطابقة |
| GL03 | قيد اليومية | partial | قيد يدوي متعدد العملات والمراكز بدورة مستقلة وأرقام وسعر تاريخي؛ تعديل رأس المسودة والسطور مع إعادة ربط الفترة وترقيم السنة والتحقق من السعر النافذ؛ حقلا العكس التلقائي المشروط وموعده؛ توليد مسودة عكس مرتبطة عند الاستحقاق وفق صلاحيات Reverse/Draft ونطاق الفرع مع منع التكرار حتى بعد إلغاء المسودة؛ مشغل مفوض اختياري لا يعتمد أو يرحّل القيد تلقائيًا؛ معاينة أثر القيد اليدوي والعكس قبل التقديم بسطور المبالغ الأصلية والمحوّلة وفرق الاتزان وفحص الموانع دون تعديل الأستاذ أو التدقيق | المشروع والطرف والمرفقات وأنواع القيود GL14؛ تفعيل عامل العكس المجدول في بيئة النشر مشروط بإعداد ActorSubject المفوض؛ لا يعد توليد مسودة العكس موافقة أو ترحيلًا |
| GL04 | قوالب القيود المتكررة | partial | قالب شهري أو ربع سنوي أو سنوي بتاريخ نهاية اختياري دون تاريخ مصطنع؛ توليد يدوي أو مجدول لمسودة واحدة لكل فترة مع مشغّل مفوض عند تفعيله؛ رقم إصدار تعديل صحيح وآخر تعديل خادمي؛ مصدر وإصدار تاريخي ومواعيد ثابتة وأسعار نافذة؛ بحث وتعطيل وسجل استخدام وتغييرات | الفرع في المخطط شرطي لكن العقد الحالي يتطلبه (GL04-F02)؛ خيارات دورية أخرى غير معتمدة؛ يلزم تفعيل المشغل على الإنتاج |
| GL05 | مراجعة واعتماد القيود | partial | قائمة مراجعة حسب الفترة والفرع والحالة والمصدر والتسلسل؛ معاينة موانع كل قيد واعتماد أو إعادة جماعية بمعاملة لكل مستند وسجل قرار محفوظ وإعادة آمنة | مراجع مستقل عن المعتمد ومراحل اعتماد متعددة؛ حالة مراجعة مستقلة عن حالة القيد وإصدار عددي للإجراء |
| GL06 | ترحيل القيود والمستندات | partial | ترحيل جماعي مستقل لكل قيد مع إيصال ومفتاح مستند وتدقيق وOutbox؛ نتائج نجاح وفشل صريحة ومعاينة وسجل رفض التحقق؛ إعادة الناجح دون تكرار وتراجع الفاشل؛ رأس دفعة دائم برقم إصدار وتوقيت خادمي وسجل إخفاق تقني خارج معاملة المستند مع استئناف آمن | توجيه مستندات المصادر الأخرى وعامل Outbox وتشغيله في الإنتاج؛ ربط سياسة مسارات الاعتماد متعددة المراحل مع SYS09 غير مكتمل؛ ملف الدولة والمصادر الخارجية لم تُنفذ |
| GL07 | عكس القيود | partial | مسودة عكس مرتبطة ثم اعتماد وترحيل؛ نسخ المبالغ التاريخية حرفيًا؛ تشغيل العكس المجدول للقيود اليدوية المرحّلة المستحقة فقط عبر فحص مفوض وفرعي مع إنشاء مسودة عكس مستقلة بلا اعتماد/ترحيل آلي؛ سجل تاريخي يمنع تكرار التوليد بعد الإلغاء؛ ترقية SQL 026 وعكس ذري قابل لإعادة المحاولة؛ معاينة أثر مسودة العكس بسعر ومبالغ تاريخية معكوسة حرفيًا وفرق اتزان قبل الاعتماد دون تعديل الأستاذ أو سجل التدقيق | عكس مستندات الدفاتر المساعدة وفحص التبعيات والضريبة؛ مشغل العكس اختياري وغير مثبت التفعيل في الإنتاج؛ الأنواع غير اليدوية تظل تعكس من مستند المصدر |
| GL08 | تقييم أرصدة العملات | not-started | — | الشاشة كاملة |
| GL09 | المصروفات المقدمة والمستحقات | not-started | — | الشاشة كاملة |
| GL10 | مطابقة الحسابات الرقابية | not-started | — | الشاشة كاملة |
| GL11 | إقفال الفترة والسنة | partial | معاينة إقفال الأستاذ بشروط مفصلة وموانع مقسمة؛ تنفيذ يعيد فحص القيود المعلقة واتزان المرحل داخل المعاملة | إقفال ERP شامل ومطابقة بنوك وذمم ومخزون وضريبة وأصول وقيد نتائج السنة وإصدار عددي للإجراء |
| GL12 | إعادة فتح فترة | partial | فتح فترة بصلاحية مستقلة وسبب مسجل | مرجع موافقة مستقل ونافذة فتح مؤقتة وإعادة إقفال تلقائي |
| GL13 | ربط القوائم المالية والتدفقات | partial | حقول رأس وبنود الربط كاملة؛ صيغ آمنة + و- ومراجع بنود مع منع الدورات والتكرار وتداخل المجموعات؛ مراجعة غير المصنف؛ لقطات أرصدة وظيفية مرحّلة ثابتة وبحث وتعطيل واستخدام وتدقيق ذري | GL13 خريطة ومراجعة لا تنفيذ تقارير RPT03/RPT04 كاملة؛ النطاق الاختياري للفرع مرشح للأستاذ ويتطلب تفويض جميع الفروع لدليل الحسابات؛ ParentCode للعرض فقط والتجميع بمعادلة صريحة؛ لا لغة ضرب/قسمة أو قواعد تدفق نقدي مفترضة |
| GL14 | أنواع القيود والمستندات | partial | GL14-01 structural foundation: JournalDocumentType with tenant-scoped branch/number-series references, unique text code, origin, attachment flag and server audit; domain-only origin compatibility and optimistic revision checks; SQL 027 atomic and idempotent; persistence rejects unvalidated SYS09 approval routes | SYS09 route schema and GL14 tenant FK exist but roles, route activation and policy validation are blocked; create/edit/disable/search/usage/audit APIs, GL03 binding, runtime enforcement of manual/automatic origin and reversal separation, and full acceptance remain unimplemented |

### شاشات CASH

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| CASH01 | الصناديق | not-started | — | الشاشة كاملة |
| CASH02 | البنوك والحسابات البنكية | not-started | — | الشاشة كاملة |
| CASH03 | سند قبض | not-started | — | الشاشة كاملة |
| CASH04 | سند صرف | not-started | — | الشاشة كاملة |
| CASH05 | تحويل بين الصناديق والبنوك | not-started | — | الشاشة كاملة |
| CASH06 | العهد والسلف التشغيلية | not-started | — | الشاشة كاملة |
| CASH07 | الشيكات الواردة | not-started | — | الشاشة كاملة |
| CASH08 | الشيكات الصادرة | not-started | — | الشاشة كاملة |
| CASH09 | حركة الشيكات | not-started | — | الشاشة كاملة |
| CASH10 | المطابقة البنكية | not-started | — | الشاشة كاملة |
| CASH11 | جرد الصندوق | not-started | — | الشاشة كاملة |

### شاشات INV

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| INV01 | المخازن والمواقع | partial | بطاقة مخزن ومواقع وحقول ثنائية اللغة وإصدار؛ أمين من عضوية فعالة في الفرع؛ بحث مقيد بالفروع وتعديل وحالة وتدقيق؛ حماية مراجع الحساب والفرع | نوع Standard/Quarantine ومرجع الأمين Membership نطاق أولي غير محسوم؛ نطاق صلاحيات مخزن مستقل والاستخدام المستندي ومتـاح البيع والحركات وقفل المواقع بعد الحركة وأولوية حساب المخزن/المجموعة غير منفذة |
| INV02 | مجموعات الأصناف | partial | شجرة مجموعات بلا دورات وربط حسابات المبيعات والمخزون والتكلفة؛ بحث وتعديل وحالة وتدقيق؛ ملخص الاستخدام المباشر والفرعي وأصنافه بصفحات مع أسباب القفل؛ حماية بنية الحساب المرتبط ومنع تعطيله مع مجموعة فعالة | مرجع الضريبة الافتراضي وسياسة تجاوز إعدادات المجموعة غير منفذين؛ استخدام الأصناف يقفل البنية وحسابا المخزون والتكلفة مطلوبان لغير الخدمة؛ لا ترحيل مخزون |
| INV03 | الوحدات والباركود | partial | دليل وحدات بأسماء ورمز قياسي ووصف ودقة كمية وإصدار عددي؛ بحث وتعديل وحالة وتدقيق؛ لا معامل تحويل عام | عرض أصناف الوحدة وقفل البنية بعد ربط ITEM_UNIT منفذان؛ تحويل الوحدة والباركود في INV04؛ لا حركة مخزون بعد؛ الرمز القياسي تحقق شكلي بلا سجل خارجي؛ دقة كمية 0..6 حد تقني معلن |
| INV04 | بطاقة الصنف | partial | بطاقة صنف ووحدات خاصة به ومعامل أساسي واحد وباركود فريد وسعر بالعملة الوظيفية؛ تحويل دقيق؛ حماية المجموعات والوحدات المستخدمة؛ بحث وحالة وتدقيق | قوائم نوع الصنف والتتبع نطاق أولي مستنتج من الغرض وINV05 وليست قائمة سياسة نهائية؛ الضريبة وحركة المخزون والدفعات والتسلسل التشغيلي والاستخدام المستندي غير منفذة؛ قفل الصنف بعد الحركة يجب إضافته مع دفتر المخزون |
| INV05 | الدفعات والأرقام التسلسلية | partial | بطاقات هوية دفعات وتسلسل؛ بحث وتعديل وحالة وتدقيق وتزامن؛ فحص التاريخ وقفل تتبع الصنف؛ دون أثر مخزني | اعتماد الجودة وإلزام الصلاحية؛ إزالة افتراض الرقم القديم عند غيابه بعقد إصدار جديد؛ الاستخدام والضمان وربط المستندات وقيود الهوية المستخدمة غير منفذة |
| INV06 | المخزون أول المدة | partial | تجهيز ومراجعة افتتاح ثم ترحيل ذري لحركة كمية وقيمة وقيد رقابة وOutbox؛ إعداد حساب التسوية ومعاينة الأثر؛ عكس مصدر معتمد بقيم تاريخية وحركات سالبة وقيد مرتبط؛ منع الافتتاح والعكس المكرر ومطابقة الإقفال وعزل الفروع | تسوية افتتاح GL02 الشاملة وإعادة افتتاح مصحح والعكس الجزئي والطباعة ومحرك تقييم الصرف؛ الأجنبية وسياسة القطع والجودة والمواقع وإدخال التسلسل النصي غير مكتملة |
| INV07 | إذن إضافة واستلام مخزني | not-started | — | الشاشة كاملة |
| INV08 | إذن صرف مخزني | not-started | — | الشاشة كاملة |
| INV09 | طلب وتحويل مخزني | not-started | — | الشاشة كاملة |
| INV10 | استلام تحويل مخزني | not-started | — | الشاشة كاملة |
| INV11 | حجز وإلغاء حجز المخزون | not-started | — | الشاشة كاملة |
| INV12 | أوامر الجرد والعد | not-started | — | الشاشة كاملة |
| INV13 | تسويات الجرد | not-started | — | الشاشة كاملة |
| INV14 | تالف وصلاحية وعزل | not-started | — | الشاشة كاملة |
| INV15 | احتساب وإعادة تقييم تكلفة المخزون | not-started | — | الشاشة كاملة |
| INV16 | أسباب الحركات المخزنية | not-started | — | الشاشة كاملة |

### شاشات SAL

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| SAL01 | بطاقة العميل | not-started | — | الشاشة كاملة |
| SAL02 | قوائم الأسعار والخصومات | not-started | — | الشاشة كاملة |
| SAL03 | أرصدة العملاء أول المدة | not-started | — | الشاشة كاملة |
| SAL04 | أمر البيع | not-started | — | الشاشة كاملة |
| SAL05 | أمر تسليم مبيعات | not-started | — | الشاشة كاملة |
| SAL06 | فاتورة المبيعات | not-started | — | الشاشة كاملة |
| SAL07 | مرتجع المبيعات | not-started | — | الشاشة كاملة |
| SAL08 | إشعار دائن أو مدين للعميل | not-started | — | الشاشة كاملة |
| SAL09 | المندوبون والمناطق | not-started | — | الشاشة كاملة |
| SAL10 | العمولات وأهداف المبيعات | not-started | — | الشاشة كاملة |
| SAL11 | تسوية وتخصيص تحصيل العملاء | not-started | — | الشاشة كاملة |
| SAL12 | متابعة التحصيل وحد الائتمان | not-started | — | الشاشة كاملة |
| SAL13 | حركات المبيعات والمرتجعات | not-started | — | الشاشة كاملة |
| SAL14 | عرض أسعار المبيعات | not-started | — | الشاشة كاملة |
| SAL15 | مجموعات العملاء وسياسات الائتمان | not-started | — | الشاشة كاملة |

### شاشات PUR

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| PUR01 | بطاقة المورد | not-started | — | الشاشة كاملة |
| PUR02 | أسعار واتفاقيات الموردين | not-started | — | الشاشة كاملة |
| PUR03 | أرصدة الموردين أول المدة | not-started | — | الشاشة كاملة |
| PUR04 | طلب شراء داخلي | not-started | — | الشاشة كاملة |
| PUR05 | طلبات عروض ومقارنة الموردين | not-started | — | الشاشة كاملة |
| PUR06 | أمر شراء | not-started | — | الشاشة كاملة |
| PUR07 | فاتورة المشتريات | not-started | — | الشاشة كاملة |
| PUR08 | مرتجع المشتريات | not-started | — | الشاشة كاملة |
| PUR09 | إشعارات المورد المالية | not-started | — | الشاشة كاملة |
| PUR10 | مطابقة الشراء والفروقات | not-started | — | الشاشة كاملة |
| PUR11 | جدولة وتخصيص سداد الموردين | not-started | — | الشاشة كاملة |
| PUR12 | دفعات الموردين المقدمة | not-started | — | الشاشة كاملة |
| PUR13 | مجموعات الموردين | not-started | — | الشاشة كاملة |

### شاشات POS

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| POS01 | إعداد نقاط البيع | not-started | — | الشاشة كاملة |
| POS02 | فتح وردية | not-started | — | الشاشة كاملة |
| POS03 | بيع نقدي سريع | not-started | — | الشاشة كاملة |
| POS04 | مرتجع نقطة البيع | not-started | — | الشاشة كاملة |
| POS05 | إقفال الوردية | not-started | — | الشاشة كاملة |
| POS06 | سجل المزامنة والأعطال | not-started | — | الشاشة كاملة |

### شاشات IMP

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| IMP01 | ملف الاستيراد والشحنة | not-started | — | الشاشة كاملة |
| IMP02 | الاعتماد المستندي | not-started | — | الشاشة كاملة |
| IMP03 | تعديلات وسحوبات الاعتماد | not-started | — | الشاشة كاملة |
| IMP04 | فواتير ومصاريف الشحن | not-started | — | الشاشة كاملة |
| IMP05 | استلام الشحنة والعجز | not-started | — | الشاشة كاملة |
| IMP06 | توزيع التكلفة وإقفال الشحنة | not-started | — | الشاشة كاملة |

### شاشات FA

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| FA01 | مجموعات الأصول | not-started | — | الشاشة كاملة |
| FA02 | بطاقة الأصل | not-started | — | الشاشة كاملة |
| FA03 | رسملة وتحسين أصل | not-started | — | الشاشة كاملة |
| FA04 | احتساب الإهلاك | not-started | — | الشاشة كاملة |
| FA05 | نقل وصيانة الأصل | not-started | — | الشاشة كاملة |
| FA06 | استبعاد وبيع الأصل | not-started | — | الشاشة كاملة |

### شاشات COST

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| COST01 | دليل مراكز التكلفة | partial | شجرة مراكز بلا دورات وبنطاق فرع متسق؛ أسماء وملاحظات ومسؤول فعال وإصدار عددي؛ تعديل وتعطيل وبحث واستخدام وتدقيق | قائمة نوع المركز غير محددة في التحليل وتنتظر توضيح المستخدم؛ الموازنات والتوزيعات وتقارير تجميع الشجرة لم تنفذ |
| COST02 | الموازنة التقديرية | not-started | — | الشاشة كاملة |
| COST03 | المناقلات وتعديل الموازنة | not-started | — | الشاشة كاملة |
| COST04 | توزيع المصروفات والتكاليف | not-started | — | الشاشة كاملة |
| COST05 | الموازنة مقابل الفعلي | not-started | — | الشاشة كاملة |

### شاشات HR

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| HR01 | ملف الموظف والعقد | not-started | — | الشاشة كاملة |
| HR02 | بنود وسياسات الرواتب | not-started | — | الشاشة كاملة |
| HR03 | سلف الموظفين والأقساط | not-started | — | الشاشة كاملة |
| HR04 | مدخلات الحضور والمتغيرات | not-started | — | الشاشة كاملة |
| HR05 | مسير الرواتب واعتماده | not-started | — | الشاشة كاملة |
| HR06 | صرف الرواتب وملف البنك | not-started | — | الشاشة كاملة |
| HR07 | تسوية نهاية الخدمة | not-started | — | الشاشة كاملة |

### شاشات MFG

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| MFG01 | تركيبة المنتج ومسار الإنتاج | not-started | — | الشاشة كاملة |
| MFG02 | أمر الإنتاج | not-started | — | الشاشة كاملة |
| MFG03 | صرف وإرجاع مواد الإنتاج | not-started | — | الشاشة كاملة |
| MFG04 | استلام المنتج والفاقد | not-started | — | الشاشة كاملة |
| MFG05 | تحميل تكلفة الإنتاج وإقفال الأمر | not-started | — | الشاشة كاملة |
| MFG06 | انحرافات الإنتاج | not-started | — | الشاشة كاملة |

### شاشات PRJ

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| PRJ01 | بطاقة المشروع والعقد | not-started | — | الشاشة كاملة |
| PRJ02 | بنود الأعمال والتغييرات | not-started | — | الشاشة كاملة |
| PRJ03 | مستخلصات العملاء | not-started | — | الشاشة كاملة |
| PRJ04 | عقود ومستخلصات المقاولين | not-started | — | الشاشة كاملة |
| PRJ05 | تكاليف المشروع والأعمال تحت التنفيذ | not-started | — | الشاشة كاملة |
| PRJ06 | المحتجزات والدفعات والضمانات | not-started | — | الشاشة كاملة |

### شاشات TAX

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| TAX01 | ملف الدولة والتسجيل | not-started | — | الشاشة كاملة |
| TAX02 | أكواد ونسب الضرائب | not-started | — | الشاشة كاملة |
| TAX03 | سجل المدخلات والمخرجات | not-started | — | الشاشة كاملة |
| TAX04 | الإقرار والتسويات الضريبية | not-started | — | الشاشة كاملة |
| TAX05 | إعداد وحدات إصدار الفواتير | not-started | — | الشاشة كاملة |
| TAX06 | متابعة الفوترة والإرسال | not-started | — | الشاشة كاملة |
| TAX07 | الاستقطاع والاحتساب العكسي | not-started | — | الشاشة كاملة |
| TAX08 | الفترات والنماذج الضريبية | not-started | — | الشاشة كاملة |

### شاشات RPT

| الشاشة | الاسم | الحالة | المنفذ | المتبقي |
| --- | --- | --- | --- | --- |
| RPT01 | ميزان المراجعة | partial | أول المدة والحركة وآخرها بالوظيفية؛ تجميع مستوى الدليل ومرشح المركز وإخفاء الصفري دون ازدواج | طرق عرض العملات ومستوى التفصيل والطباعة والتصدير وتحسين أحجام كبيرة |
| RPT02 | دفتر الأستاذ وكشف الحساب | partial | حساب واحد برصيد افتتاحي وجارٍ صحيح عبر الصفحات؛ مرشح مركز ومرجع خارجي يطبقان على الافتتاح والحركة | نطاق حسابات وتصدير وطباعة وطرق عرض إضافية |
| RPT03 | القوائم المالية | not-started | — | الشاشة كاملة |
| RPT04 | التدفقات النقدية | not-started | — | الشاشة كاملة |
| RPT05 | تحليل المبيعات والربحية | not-started | — | الشاشة كاملة |
| RPT06 | أعمار ديون العملاء | not-started | — | الشاشة كاملة |
| RPT07 | أعمار ومستحقات الموردين | not-started | — | الشاشة كاملة |
| RPT08 | أرصدة وتقييم المخزون | partial | رصيد وحركة وقيمة افتتاح المخزون وعكسه حسب المخزن والصنف والهوية والرقابة؛ إجماليات قبل الصفحات وعزل الفروع | افتتاح وعكسه فقط؛ لا حجز/متاح/طريق/عزل ولا محرك تكلفة أو مطابقة رقابة آلية عامة؛ لا عملة عرض أو طباعة أو تصدير؛ حتى تاريخ يستخدم to نفسه؛ التجميع ثابت |
| RPT09 | بطاقة الصنف والحركة | partial | حركة افتتاح وعكس بوحدة أساسية ورصيد جارٍ عبر الصفحات؛ وحدات وتكلفة سطور المصدر التاريخية وروابط المستند والقيد؛ ترتيب تاريخ الأثر ووقت الترحيل | المصادر الحالية افتتاح وعكسه فقط؛ لا صرف/استلام/تحويل؛ لا عملة عرض أو طباعة أو تصدير؛ التفصيل ثابت؛ تجميع القراءة في الذاكرة يحتاج تحسينًا للأحجام الكبيرة |
| RPT10 | الراكد والصلاحية وإعادة الطلب | not-started | — | الشاشة كاملة |
| RPT11 | سجل الأصول والإهلاك | not-started | — | الشاشة كاملة |
| RPT12 | لوحة الإدارة والاستثناءات | not-started | — | الشاشة كاملة |
| RPT13 | كشف حساب العميل ومصادقة الرصيد | not-started | — | الشاشة كاملة |
| RPT14 | كشف حساب المورد ومصادقة الرصيد | not-started | — | الشاشة كاملة |

### المخططات الـ28

| المخطط | الحالة | حدود التنفيذ |
| --- | --- | --- |
| [module-SYS](../../muhaseb-erp-analysis/diagrams/source/module-SYS.mmd) | partial | تهيئة أساسية فقط؛ راجع صفوف SYS في مصفوفة الشاشات |
| [module-GL](../../muhaseb-erp-analysis/diagrams/source/module-GL.mmd) | partial | دليل حسابات مع تحرير مضبوط وطبيعة صريحة وتدقيق؛ أستاذ وقوالب وتوليد مسودات؛ باقي وظائف GL غير مكتملة |
| [module-CASH](../../muhaseb-erp-analysis/diagrams/source/module-CASH.mmd) | not-started | لم يبدأ التنفيذ |
| [module-INV](../../muhaseb-erp-analysis/diagrams/source/module-INV.mmd) | partial | وحدات INV03 ومجموعات INV02 وبطاقات INV04 وتحويلات وباركود وحماية المراجع وعرض الاستخدام؛ بطاقات مخازن INV01 ومواقعها؛ الحركات والتكلفة والتتبع التشغيلي لم تنفذ |
| [module-SAL](../../muhaseb-erp-analysis/diagrams/source/module-SAL.mmd) | not-started | لم يبدأ التنفيذ |
| [module-PUR](../../muhaseb-erp-analysis/diagrams/source/module-PUR.mmd) | not-started | لم يبدأ التنفيذ |
| [module-POS](../../muhaseb-erp-analysis/diagrams/source/module-POS.mmd) | not-started | لم يبدأ التنفيذ |
| [module-IMP](../../muhaseb-erp-analysis/diagrams/source/module-IMP.mmd) | not-started | لم يبدأ التنفيذ |
| [module-FA](../../muhaseb-erp-analysis/diagrams/source/module-FA.mmd) | not-started | لم يبدأ التنفيذ |
| [module-COST](../../muhaseb-erp-analysis/diagrams/source/module-COST.mmd) | partial | شجرة مراكز ومسؤول وتعديل وتعطيل؛ نوع المركز والموازنات والتوزيعات لم تكتمل |
| [module-HR](../../muhaseb-erp-analysis/diagrams/source/module-HR.mmd) | not-started | لم يبدأ التنفيذ |
| [module-MFG](../../muhaseb-erp-analysis/diagrams/source/module-MFG.mmd) | not-started | لم يبدأ التنفيذ |
| [module-PRJ](../../muhaseb-erp-analysis/diagrams/source/module-PRJ.mmd) | not-started | لم يبدأ التنفيذ |
| [module-TAX](../../muhaseb-erp-analysis/diagrams/source/module-TAX.mmd) | not-started | لم يبدأ التنفيذ |
| [module-RPT](../../muhaseb-erp-analysis/diagrams/source/module-RPT.mmd) | partial | ميزان وأستاذ جزئيان |
| [system-map](../../muhaseb-erp-analysis/diagrams/source/system-map.mmd) | partial | أساس SYS وGL وبعض COST وINV وRPT؛ بقية الأقسام لم تبدأ |
| [document-lifecycle](../../muhaseb-erp-analysis/diagrams/source/document-lifecycle.mmd) | partial | الدورة منفذة للقيد اليدوي؛ الرفض يعيد Draft مع سبب وتاريخ في التدقيق؛ محاولة الترحيل ليست حالة دائمة |
| [purchase-to-pay](../../muhaseb-erp-analysis/diagrams/source/purchase-to-pay.mmd) | not-started | لم يبدأ التنفيذ |
| [sale-to-cash](../../muhaseb-erp-analysis/diagrams/source/sale-to-cash.mmd) | not-started | لم يبدأ التنفيذ |
| [stock-transfer](../../muhaseb-erp-analysis/diagrams/source/stock-transfer.mmd) | not-started | لم يبدأ التنفيذ |
| [returns](../../muhaseb-erp-analysis/diagrams/source/returns.mmd) | not-started | لم يبدأ التنفيذ |
| [period-close](../../muhaseb-erp-analysis/diagrams/source/period-close.mmd) | partial | فحص القيود المعلقة واتزانها فقط؛ باقي مسار المطابقة والإقفال السنوي غير منفذ |
| [einvoice](../../muhaseb-erp-analysis/diagrams/source/einvoice.mmd) | not-started | لم يبدأ التنفيذ |
| [posting-sequence](../../muhaseb-erp-analysis/diagrams/source/posting-sequence.mmd) | partial | ذرية القيد وOutbox والتدقيق؛ لا دفاتر مساعدة أو عامل إرسال خارجي بعد |
| [imports](../../muhaseb-erp-analysis/diagrams/source/imports.mmd) | not-started | لم يبدأ التنفيذ |
| [extensions](../../muhaseb-erp-analysis/diagrams/source/extensions.mmd) | not-started | لم يبدأ التنفيذ |
| [opening-migration](../../muhaseb-erp-analysis/diagrams/source/opening-migration.mmd) | not-started | لم يبدأ التنفيذ |
| [data-model](../../muhaseb-erp-analysis/diagrams/source/data-model.mmd) | partial | شركات وفروع وحسابات وفترات وقيود وسطـور وتدقيق وOutbox؛ امتداد قوالب دورية بإصدار وتوليد ووحدات ومجموعات وITEM/ITEM_UNIT وWAREHOUSE/Locations؛ DOCUMENT والدفاتر المساعدة لاحقًا |

### قواعد الأعمال الـ20

| القاعدة | النطاق | المنفذ والمتبقي | الدليل |
| --- | --- | --- | --- |
| BR01 | manual-journal | Decimal واتزان دقيق؛ رفض فقدان الدقة؛ فروق التقريب تحتاج سطرًا صريحًا | Program.cs: BR01 / Wire contract |
| BR02 | partial | شركة وفرع وحساب ومركز وعملة وسعر ووحدة ومجموعة وصنف؛ مخازن ومواقع؛ لا أطراف أو ضرائب بعد | Program.cs: BR02; SqlIntegration.cs: company isolation |
| BR03 | manual-journal | تاريخ الوثيقة والأثر ووقت الترحيل مستقلة؛ فترة مفتوحة | Program.cs: BR03 |
| BR04 | partial | حساب فعال فرعي وعملة ومركز صحيح؛ المشروع والطرف لاحقًا | Program.cs: BR04 |
| BR05 | partial | ذرية القيد والتدقيق وOutbox والنتيجة والتوليد الدوري وافتتاح المخزون؛ بقية الدفاتر والعامل الخارجي لم تنفذ | Program.cs: BR05; SqlIntegration.cs / RecurringSqlIntegration.cs: constraint rollback |
| BR06 | partial | Idempotency per company/actor/operation/body؛ قيد دوري فريد لكل قالب وفترة؛ مصادر بقية المستندات لاحقًا | Program.cs: BR06; RecurringTests.cs; SqlIntegration.cs / RecurringSqlIntegration.cs: concurrency |
| BR07 | partial | حماية المرحل وعكس مرتبط تاريخي للقيد وافتتاح المخزون بكمية وقيمة وقيد ذري؛ بقية المصادر لاحقًا | Program.cs: BR07; InventoryOpeningReversalTests.cs; InventoryOpeningSqlIntegration.cs |
| BR08 | partial | منع قيد يدوي للحساب الرقابي؛ مطابقة افتتاح المخزون وحركته مع قيده عند الإقفال؛ بقية الدفاتر لاحقًا | Program.cs: BR08 |
| BR09 | partial | تدقيق الأوامر وفصل المنشئ والمعتمد؛ مسارات مراجعة متعددة لاحقًا | Program.cs: BR09 / Workflow |
| BR10 | manual-journal | سعر موجب مؤرخ ثابت ومبالغ أصلية ووظيفية وعكس تاريخي | Program.cs: BR10 |
| BR11 | not-started | لم يبدأ التنفيذ | none |
| BR12 | not-started | لم يبدأ التنفيذ | none |
| BR13 | not-started | لم يبدأ التنفيذ | none |
| BR14 | partial | منع الإقفال مع قيود/افتتاحات معلقة أو افتتاح مخزون غير مطابق لقيده؛ الإقفال الشامل والسنوي لاحقًا | Program.cs: BR03; SqlIntegration.cs: close/post race |
| BR15 | partial | تقارير الأستاذ تستبعد المسودات؛ لا تقارير ذمم منفذة | Program.cs: RPT01 RPT02 |
| BR16 | not-started | لم يبدأ التنفيذ | none |
| BR17 | not-started | لم يبدأ التنفيذ | none |
| BR18 | partial | عزل الشركات والفروع وعضويات حالية؛ نطاق الحساب والسعر والتصدير لاحقًا | Program.cs: BR18 / SYS06 |
| BR19 | not-started | لم يبدأ التنفيذ | none |
| BR20 | not-started | لم تنفذ ضرائب أو ملفات تشريعية أو تكاملات دولة | none |

<!-- generated-progress:end -->
