# SYS07 role catalog dependency for SYS09 — structural foundation, not live authorization

Source of truth: `muhaseb-erp-analysis/docs/modules/SYS.md` SYS07 F01–F14 and the original `fields.csv`. No analysis, source diagram or field was changed.

## Implemented

- `SecurityRole` header: tenant company, optional branch, server active state, required role name and responsibility, actor and creation/last-modified timestamps, integer revision distinct from the concurrency UUID.
- `SecurityRoleRule`: tenant role, screen code, operation code, explicit allowed/denied flag, optional six-place decimal amount ceiling and matching tenant currency reference. Amount and currency must appear together, and denial cannot carry an amount cap.
- Domain validation rejects invalid references, malformed codes, invalid decimal precision, duplicate rule keys and stale revision timestamps without mutating the owner. A conservative pure `Permits` evaluator refuses implicit grants, refuses mismatched amount/currency and gives explicit deny precedence. **This evaluator is not wired to user authorization.**
- EF composite company FKs to branch, role and currency; unique rule key per company/role/screen/operation/currency. SYS09 `ApprovalRouteStage.ApproverRoleId` now has a tenant-scoped FK to `SecurityRoles`.
- Forward-only `029_security_role_foundation.sql`: atomic schema lock, 028 prerequisite, empty tables, stage role FK, version ledger and repeatable no-op. No roles, users, permissions, workflows or policies are seeded.
- EF guards refuse all role/rule writes until authorization and branch scope are enforced. Existing SYS09 and GL14 write guards remain intact; legacy membership-based authorization is unchanged.

## Incomplete, not accepted

SYS07 role CRUD, user-role assignments, explicit-deny enforcement across multiple roles, branch/amount/currency authorization at the API boundary, distinct cost/export/posting permission policy, concurrency-safe change audit, search, usage and acceptance are **not implemented**. Role names and operation codes are structural strings, not a finalized policy registry. SYS09 also requires active role and branch compatibility, allowed document-kind values, overlap and amount/currency policy, staged approvals and notifications. The FK alone cannot make SYS09 or GL14 operational.

## Next

1. Define and enforce membership-to-role assignments and explicit-deny priority with branch scope, transaction-safe auditing and concurrency. Preserve current explicit membership grants until migration behavior is accepted.
2. Validate active role and branch compatibility for SYS09 stages, document-kind registry and conflict-free amount ranges with currency policy; only then open guarded SYS09 endpoints.
3. Finish GL14 endpoints and GL03 integration with positive/negative acceptance. Do not invent policies or seed production data.

## Verification

Local .NET 10 Release, domain/EF, existing HTTP/JWT/OpenAPI and traceability: **342/342 passed, zero failures**. SQL 029 upgrade, rollback, idempotency and tenant FK tests are wired into the disposable SQL CI suite and **must be reported as pending until that CI run passes**. The tests do not touch production databases.
