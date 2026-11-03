# GL03-02 — original journal-entry screen completeness and acceptance audit (2026-11-03)

**Scope:** Notion GL03-02 is a bounded **screen-completeness audit** of GL03, not implementation or closure of GL03-01 or any pending field/feature. Original sources muhaseb-erp-analysis/docs/modules/GL.md and muhaseb-erp-analysis/data/fields.csv remain unchanged. Baseline gl SHA ded2564ad77bfda6cb0b5c0c4edc24fc49cd59df descends from merged four-agent main SHA f478e9ceef5816f90e6c20c5438dd641eedfb8d6 (main CI #41664426830: **464/464 PASS**; GL01-02 PR #67 CI #41665909625: **464/464 PASS**). This documentation update independently requires fresh exact-head CI.

## Field-by-field implementation classification

Traceability source: docs/backend/fields-implementation.csv, IDs GL03-F01 through GL03-F28, cross-checked against the 28 original rows in the GL.md GL03 section.

| Coverage | Source field IDs | Persisted behavior or blocker |
|---|---|---|
| **Implemented (22)** | F01/F02/F03; F05/F06/F07; F10/F11/F12; F14/F15; F16/F17/F18/F19/F20/F21; F24/F25/F26/F27/F28 | Tenant/branch, journal identity/document/date/number/description/state, external reference, automatic reversal intent/date, lines with debit/credit/currency/historical rate/cost-center/description, creator/revision/last-modified metadata. Revision integer differs from Version UUID. |
| **Derived (1)** | F04 | Fiscal year is derived from selected FiscalPeriod.FiscalYearId; avoid redundant mutable journal fiscal-year state. |
| **Partial (3)** | F08/F09/F13 | F08/F09: line-level historical currency/rate are stored, but required header currency/rate is undefined for a mixed-currency document. F13: manual/recurring/reversal provenance exists, but approved configured type catalog and runtime authorization/approval integration require GL14/SYS09. |
| **Pending (2)** | F22/F23 | Project and party per-line references depend on authorized PRJ01 / SAL/PUR reference records and an approved binding contract. No invented GUID-only placeholders. |

**28 = 22 implemented + 1 derived + 3 partial + 2 pending. GL03 screen stays PARTIAL.**

## Action, security, SQL and acceptance evidence

| Original action / guard | Existing evidence and limits | Missing for full screen |
|---|---|---|
| Search, new, draft, submit, approve, post by source | JournalService, JournalsEndpoints; JournalProcessingTests.cs, JournalProcessingSqlIntegration.cs, HostedApiIntegration.cs: production HTTPS/JWT/OIDC and disposable SQL test lifecycle, idempotent replay, branch/tenant authorization, document numbering and outbox | GL14 journal-type configuration and runtime approval routing for all enabled types |
| Financial effect and trial balance | BalancedJournalLedgerSqlAcceptance.cs; JournalImpactPreviewTests.cs and JournalLineGuardAcceptance.cs: a posted balanced manual entry appears once in both account ledgers and trial balance; preview is read-only | Cross-ledger fiscal close, subsidiary posting and reconciliations in GL11/GL06/GL10 |
| Historical header and line changes | JournalHeaderFieldsTests.cs, JournalHeaderFieldsSqlIntegration.cs, JournalHeaderFieldsHttpIntegration.cs: dates, period scope, currency snapshots, revision and atomic audit | Undefined header currency/rate for mixed lines, project and party references, approved attachment integration |
| Print and linked reversal | JournalPrintSqlIntegration.cs, JournalPrintTests.cs, JournalAuditTests.cs, HostedApiIntegration.cs: authorized read-only HTML/RTL for manual and linked manual reversal, original number, reason and historical rates | Other source-owned SAL/PUR reversals and tax/dependency checks remain GL07/GL06; browser HTML is not signed SYS12 document |
| Scheduled reversal | ScheduledReversalTests.cs, ScheduledReversalSqlIntegration.cs, ScheduledReversalHostedIntegration.cs: intent/date, guarded due runner creates reviewable **draft** | Deployed worker activation not demonstrated; no automatic unapproved posting |
| Negative acceptance | JournalLineGuardAcceptance.cs, JournalUnbalancedPostingSqlIntegration.cs, JournalClosedPeriodSqlIntegration.cs, hosted JWT: reject invalid single-sided lines, unbalanced entries, direct protected control posting, closed period, cross tenant or source impersonation | Project/party/attachments, unconfigured GL14 types and all subsidiary source dependencies |

Original control: exactly one positive debit/credit side per line and no direct protected control posting except permitted source. Relevant rules BR02/BR09/BR18/BR01/BR03/BR05/BR06/BR07/BR10. Dependencies GL01/COST01/PRJ01, GL14/SYS09, SAL/PUR, GL06/GL07, source receipt and tax safeguards.

## Exact acceptance boundary and open tasks

1. **Complete GL03-02 as audit only** when all 28 fields and required actions have evidence or exact blockers as above; unchanged implementation-map/traceability is rechecked; new SHA GitHub CI succeeds with isolated SQL Server and HTTPS/JWT/OIDC/OpenAPI and analysis/progress hashes; Notion receives PR/SHA/CI.
2. **Do not close GL03-01, GL03-FG03/FG04/FG06 or GL03-GP01/GP02/GP03/GP04.** These require implementation of header currency/rate, configured types, project/party, attachments or independent source policies. Their implementation must not be claimed from the audit.
3. GL03 screen remains **partial**. No changes to core code, SQL schema, API, accounting policy, original source analysis, other agents' worktrees or production data.
