# SYS07/SYS08 — guarded role snapshot integrity (2026-11-02)

Task: SYS07-GAP01 / SYS07-01, dependency for SYS09 and GL14. This is a **partial, domain-only** change. The approved SYS07 analysis requires explicit denial to take precedence, company-scoped references, and independent cost/export/post operations. It does not provide a complete approved per-screen operation catalog; no registry or authorization endpoint was invented.

## Implemented

- The pure `SecurityRolePolicyEvaluator` refuses an empty or incomplete assigned-role snapshot instead of silently dropping a missing denial.
- It rejects cross-company membership-role links, duplicate role links, and assigned role/rule company or role mismatches.
- It trims and normalizes requested screen and operation codes consistently.
- Tests cover missing assigned denial, foreign link, duplicate link, extra unassigned role, and normalized valid grant.

No new SQL migration. No change to original analysis. Existing EF write guards remain in place. This evaluator is **not** wired to runtime JWT authorization and does not grant production access.

## Validation and remaining work

Local .NET 10 Release, traceability, domain/EF tests, HTTP/JWT and OpenAPI: **345/345 passed, zero failed**, with no live SQL connection. Evidence: `backend/.artifacts/sys07-snapshot-20261102/verification.json`. Branch CI [#41430688379](https://github.com/ameenalqershi/ameen-erp/actions/runs/41430688379) passed **346/346**, zero failures, including isolated SQL Server, HTTP/JWT, OpenAPI and Release. Source SHA256: `1133ca97eb94db6fafe18be8fd41a860fc9afa5c804ff6e5ec508bae0a216513`. Main CI remains to be verified after integration.

Next: obtain/derive only explicitly approved screen-operation catalog; implement audited SYS07 role and SYS08 assignment administration, concurrency, anti-self-escalation, complete tenant-scoped snapshot loading, and negative HTTP/JWT acceptance. Then SYS09 stage policy and GL14/GL03. SYS07/SYS08/SYS09/GL14 remain partial.
