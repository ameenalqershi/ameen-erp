# AMEEN ERP — Dependency-Aware Parallel Execution Protocol
> Status: proposal for SUPERVISOR review. This document is process guidance, not approval to invent accounting policies or activate blocked functionality.
> Notion playbook: https://app.notion.com/p/40d3830ba34d818fac39e6b91d3f6bc7

## Why
Normal ChatGPT agent windows assigned to CASH, SYS, GL, INV, SAL, CORE and other ERP areas encounter partially blocked parent tasks. The right response is **split the work at genuinely independent acceptance boundaries**, preserve the integration dependency, and pick another ready unit in the agent's own area. Avoid claiming a screen is complete because a technical foundation passed CI.

## Source of truth and required preflight
1. Read AGENTS.md, docs/backend/PROGRESS.md, docs/backend/fields-implementation.csv, relevant muhaseb-erp-analysis screen/rules/diagrams and Notion numbered tasks. Never alter source analysis.
2. Inspect current main, your domain branch, open PRs/worktrees and exact-head CI before any changes; do not duplicate another agent's work.
3. Notion's existing numbered task database already has **مرجع المهمة الأم**, **يعتمد على مهام**, **مستوى المهمة**, **الحالة**, **التبعيات**, **شرط الإغلاق والتنفيذ المبكر**, **المتبقي للاكتمال**, and the execution/evidence fields. Use this database, not a second tracker.

## Dependency triage
Classify a blocker explicitly:
- **PROVIDER_PENDING** — another module/PR is not accepted or merged.
- **CONTRACT_PENDING** — a data/API/permission/event/source contract needs agreement.
- **POLICY_UNDEFINED** — enum, posting, currency, audit, accounting or approval policy not sanctioned by the source.
- **CI_REVIEW_PENDING** — code exists but final SHA or integration verification is absent.
- **FILE_OWNERSHIP_CONFLICT** — another agent owns the area or worktree.
- **READY** — independent scope with complete acceptance evidence.

The mere presence of a domain class, FK, stub, or passing unit test does not mean a provider is operationally ready.

## When to create child tasks
First reuse any numbered atomic/acceptance/gap tasks already present. Only create a child for a new, distinctly testable deliverable with:
- Unique child identifier (e.g. `CASH04-01-PRE01`, `CASH04-01-INT01`, `CASH04-01-AC03`), after checking for collisions.
- Exact parent reference, original requirement/field IDs and specification URL.
- Typed dependency relation to the existing provider tasks where confirmed; named blocker and its owner where unresolved.
- A **narrow acceptance criterion** and explicit exclusions of overall parent/screen completion.
- Owner domain and branch, PR, final SHA, tests/CI, remaining work, reviewer outcome and resumption condition.
Do not fan out thousands of planning tasks. As a guideline, split a blocked parent into 2–4 child outcomes only where genuinely necessary.

## Three delivery paths
1. **PRE (safe independent work):** read-only contract audit, validated schema/constraint/authorization/tenant tests, or other self-contained work proven by the original spec. Never create misleading polymorphic data models or incomplete mandatory business semantics.
2. **INT (dependent integration):** workflow / API / posting / approvals dependent on source owner. Keep fail-closed guards and production side effects disabled until the contract is accepted.
3. **AC (end-to-end acceptance):** positive/negative, concurrency/idempotency, tenant/branch isolation, security, SQL Server, hosted HTTP/JWT/OIDC, OpenAPI and ledger impact as applicable. Passing PRE is not passing INT or AC.

Never replace an unknown policy with a guessed default. If no safe PRE work exists, record the blocker precisely and move to the next READY task *inside the same domain*. Do not spin on a blocked row or repeatedly rediscover its policy gap.

## Source-provider handoff
The provider agent records its contract, constraints, migration/API/event schema, exact accepted PR/SHA/CI and any known unresolved decisions. Consumers link to that task, implement against sanctioned contracts only, and leave dependent execution blocked until acceptance. Do not edit another domain's code without coordination. Escalate policy gaps to SUPERVISOR or the user as a distinct decision; never invent policy.

## GitHub and Notion gates
- Use a unique `temp/<domain>-<task>-<stage>` branch and PR under current repository/agent branch conventions. A domain agent never merges directly to main. SUPERVISOR alone reviews integration order and merges qualified batches.
- Preserve the original analysis and production data. Keep CI/security/tenant/compliance gates intact. Update tests, traceability, PROGRESS.md and OpenAPI where applicable.
- A child can become **مكتمل** only for its OWN acceptance evidence. The parent/screen stays **جزئي**, **محجوب** or **قيد التنفيذ** until its full acceptance closes.
- In Notion update: الحالة، مرحلة البناء، التبعيات، المتبقي للاكتمال، شرط الإغلاق والتنفيذ المبكر، نتيجة مراجعة الإنجاز، تنسيق التنفيذ، معرّفات المتطلبات، مرجع المهمة الأم، and dependency links as evidenced.
- Report to SUPERVISOR: parent + child IDs, classification, blocker/owner, branch, PR, final SHA and CI, tested boundaries, remaining work, and precise condition to resume.

## Grounded example: CASH04-01 — Payment voucher
Source: muhaseb-erp-analysis/docs/modules/CASH.md, section CASH04. Parent: https://app.notion.com/p/4053830ba34d81c1bf69d4ab3dfd042a
The parent is currently blocked by beneficiary and invoice/account mapping, approved vendor bank account rules, PUR11, CASH08, CASH01/CASH02, GL06, SYS03/SYS07.
- **Possible PRE:** a *read-only* dependency / field contract audit or an already-specified standalone constraint test after checking existing coverage.
- **Blocked INT:** actual voucher model/API, approval, payment, accounting posting and source references where contracts are missing.
- **Blocked AC:** cross-module realistic payment/negative/rollback/tenant tests after provider acceptance.
Never invent a beneficiary union model, payee bank rule, balance availability rule, or live posting merely to make the row look implemented.

## Important runtime limit
Ordinary ChatGPT conversation tabs do **not** self-trigger endless turns or message other tabs autonomously. This protocol reduces decision stalls; continuous reactivation still requires the user's interaction or a separately authorized coordinator/browser automation.
