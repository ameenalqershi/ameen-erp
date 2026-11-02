# SYS07/SYS08 user-to-role association and pure deny-first evaluator — guarded partial dependency

Source: muhaseb-erp-analysis/docs/modules/SYS.md, SYS07 F01–F14, SYS08 F04–F05 and F06. The original analysis and diagrams remain unchanged.

## Implemented in this batch

- SecurityRoleAssignment links a company membership to a company role; both composite tenant FKs, unique (CompanyId, MembershipId, RoleId), and immutable references. SQL 030 is transactional, requires 029, is repeatable and seeds no users/roles/grants.
- EF SaveChanges rejects every assignment insert/update/delete with security_role_assignment_policy_unavailable. The existing SYS07 role/rule, SYS09 route and GL14 guards remain unchanged.
- Pure SecurityRolePolicyEvaluator.Permits combines explicitly supplied assigned roles: inactive membership or role, wrong tenant, branch outside membership or role, absent assignment, negative amount and missing permission fail closed. Any explicit deny in an applicable assigned role wins over all grants; amount/currency caps apply to positive grants. Different operations remain independent.
- Tests cover cross-role deny, amount and currency limits, tenant/branch isolation, inactive membership, missing assignments, EF FK model and blocked writes. Disposable SQL CI additionally verifies migration rollback/retry, FK rejection, uniqueness and no seeding.

## Explicit limitations and security boundary

This is not an authorization service. The evaluator cannot prove that a caller supplied a complete, consistent snapshot of roles, rules and assignments; it must never be used to grant API access without a transactionally consistent tenant-scoped loading strategy and acceptance tests. Existing membership grants are unchanged and still authorize existing APIs. No user-to-role API, role CRUD, grant migration, audit trail, concurrency-safe policy edits, SYS09 active-role approval or GL14 endpoints are available. Do not remove any write guard or deploy a migration to production from this test.

## Next safe acceptance steps

1. Define an audited role/assignment administration service with optimistic concurrency, explicit permission registry (including cost/export/post independence), active branch/currency validation, tenant-snapshot loading and membership self-escalation protection.
2. Add negative HTTP/JWT tests for cross-company/branch/role, explicit deny, amount caps, concurrent edits, revocation, and replay. Verify SQL isolated rollback and audit atomicity before opening write guards.
3. Complete SYS09 active-role/overlap/stage policy and GL14/GL03 integration; do not claim either module accepted yet.
