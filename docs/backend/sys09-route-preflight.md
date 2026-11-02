# SYS09 — Conservative route-conflict preflight and atomic stage edits (partial)

## Original requirements and boundary

`muhaseb-erp-analysis/data/screens.csv` SYS09 forbids conflicting approval routes for the same document and scope. `fields.csv` SYS09-F02/F04–F11/F14–F15 defines optional branch, policy-defined document type, inclusive monetary range in decimal(20,6), currency, ordered stages, alert delay, revision and server audit. **No original analysis or diagram was changed.** This work does not invent document-kind values, exchange-rate rules, approval timeout behavior, or any new permission.

## Domain-only implementation

- `ApprovalRouteConflictPolicy.Evaluate` checks a supplied tenant snapshot without modifying it. A company-wide route overlaps any specific branch; two distinct specific branches do not. Only the same document type is compared. Same-currency intervals have inclusive endpoints, with `null` maximum treated as unbounded. Inactive existing routes are ignored; the candidate is always evaluated as a hypothetical activation. The candidate's own ID is excluded for edit preflight.
- Any cross-currency route with the same document and overlapping branch scope yields `UnresolvedCurrency` regardless of the raw numeric ranges; no arbitrary FX conversion or currency equivalence is inferred. `Overlap` takes precedence if a same-currency conflict also exists. Null, duplicate, cross-company or malformed snapshot rows return `InvalidSnapshot` instead of false assurance.
- `ApprovalRoute.ReplaceStages` constructs and validates the entire new stage set before modifying the aggregate. It rejects null drafts, invalid references, invalid stage numbers/alert delays, duplicate numbers, stale revisions and invalid timestamps without partial changes. Expected-revision overloads for route header editing and status changes similarly reject stale writes. Existing SQL/EF write guards are preserved.

## Acceptance boundary and next dependency

This is **not** a runtime collision guarantee: an in-memory enumerable cannot prove tenant snapshot completeness or serializable transaction isolation. No SYS09 persistence, API, JWT permission, role eligibility, stage execution, timeout auto-approval, GL14 activation or GL03 routing is enabled. The original document-type allowed list, currency comparison policy, active role membership and transactional conflict arbitration remain blockers. Future writes must enforce these policies under database concurrency and audit before removing the EF guards. No SQL migration or production data change is part of this batch.
