# GL06-04 — posting and document-batch screen completeness audit (2026-11-03)

**Scope:** Notion GL06-04 is a bounded completeness review of the original GL06 specification and its present code/tests, not an implementation or closure of GL06-01 or GL06-GP01. Original source muhaseb-erp-analysis/docs/modules/GL.md, source field dictionary and diagrams remain untouched. Baseline gl SHA 5477da2bea6b248f4f303fd95ab3bd53c54ca23b contains merged main SHA f478e9ceef5816f90e6c20c5438dd641eedfb8d6 and the validated GL01/GL11 acceptance. The last fully passing gl CI #41667836488 had **464/464** tests; this audit commit requires its own branch/PR/post-merge CI.

## Original fields and implementation traceability

Source section GL06 in muhaseb-erp-analysis/docs/modules/GL.md and GL06-F01..GL06-F13 entries in docs/backend/fields-implementation.csv:

| Field group | IDs | Current source of values and actual limitation |
|---|---|---|
| **Implemented — 12 fields** | F01, F02, F03, F05, F06, F07, F08, F09, F10, F11, F12, F13 | Company route and authorized branch, journal state, bounded JV number range (not arbitrary unrestricted numeric pattern), posted execution time, source journal ID, per-document preview/result and receipt, server-recorded batch actor/time/revision/last-modified. Persisted batch audit identity is distinct from individual journal audit. |
| **Partial — 1 field** | F04 | Journal source/provenance can be selected/read inside the processing queue, but routing and processing **other source documents** plus worker-delivered Outbox still require an approved owner and execution policy. The presence of inventory-opening posting via its source endpoint does not make GL06 a universal document router. |

**Total 13 = 12 implemented + 1 partial. GL06 screen remains PARTIAL.**

## Original actions and acceptance controls

| Original analysis action / guard | Current evidence / scope | Unclosed |
|---|---|---|
| Select scope, preview, validate | Journal-processing queue and processing-preview by authorized company, branch, status, journal source and bounded JV number range; JournalProcessingTests.cs and HostedApiIntegration.cs validate selected scope/validation outcomes | No arbitrary cross-source/ERP router, no unapproved free-form number expression |
| Approve only when effect, execute atomic document | GL05 journal approval state precedes individual journal post; JournalProcessingSqlIntegration.cs, JournalProcessingTests.cs and HostedApiIntegration.cs verify posting, per-item receipt, version, durable idempotency and Outbox record, atomic rollback on failed items and safe same-key retry | Activation of worker delivery and policy for additional SAL/PUR/CASH/FA source documents remains outside this proven journal batch |
| Show outcome and execution history | Persisted JournalProcessingBatch/JournalProcessingRow and processing-history / processing-batches/by-key endpoints; caller-isolated review/result, per-item success or rejection with error code and source journal references; SQL and JWT tests for paging, ownership and zero read-side effects | A successful row must never be treated as evidence that all rows succeeded; an external delivery failure must not be mislabeled as successful posting |
| Positive acceptance | An approved in-scope journal is posted **once**, exact financial effect, audit/receipt/outbox persisted, replay does not duplicate | End-to-end delivery by a running Outbox worker or other-document post routes not demonstrated |
| Negative acceptance | Mixed valid/invalid batch yields independent durable results; unauthorized tenant/branch, invalid/locked journal, replay with conflicting payload, failed SQL persistence and direct control-account posting are refused with documented outcome | Source-specific reconciliation/tax and new workers depend on original approved policies and other systems |

Original shared rules BR02/BR09/BR18. Dependencies GL05 approval decisions, SYS14 audit, SYS07 tenant authorization, GL06-GP01 source/worker policy. The source mandates **one document per atomic transaction**, not a false aggregate-success flag. Separate source posting services may have their own safety guards but are not a substitute for the GL06 routing contract.

## Acceptance and follow-up

- GL06-04 **audit only** is complete once all 13 fields and actions have exact code/test links or transparent missing dependencies; source hashes and traceability/progress scripts pass; new SHA GitHub CI passes isolated SQL and production HTTPS/JWT/OIDC/OpenAPI; Notion records PR, CI, scope and blockers.
- GL06-01 / GL06-GP01 and still-pending GL06-FG01 or GL06-T01 checks are **not** closed by this review. Existing completed GL06-GP02/GP03 and their evidence are reused rather than reimplemented.
- No changes in accounting policy, posting API, data schema, source analysis, other agent worktrees or production data. GL06 remains **partial** until authorized source routing and worker delivery are fully implemented and tested.
