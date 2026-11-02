# SYS07-GAP01 — Atomic role policy draft edits (partial, guarded)

Source of truth: muhaseb-erp-analysis/docs/modules/SYS.md SYS07 F01–F14 and SYS08. Original analysis is unchanged.

## Scope

- Added SecurityRole.ReplaceRules accepting complete rule drafts, checking expectedRevision first, validating every new SecurityRoleRule (screen/operation code, explicit deny, six-decimal amount and currency pairing), and checking duplicate normalized operation/currency tuples **before mutating** the role. A successful replacement advances revision/timestamp exactly once.
- Added revision-aware overloads for role header edit and activation; legacy overloads remain for existing domain consumers, **not** accepted API concurrency protection.
- Domain regression verifies no mutation on invalid rule, duplicate, or stale revision; separate POST, EXPORT, COST decisions and exact amount cap; accepted deactivation increments revision.

## Security and acceptance limits

This is a **domain-only preparation**, not role administration. There are no new endpoints, no role/assignment writes (EF guards remain), no approved screen/operation registry, no audit service, no transactional tenant snapshot, and no authorization replacement. The existing membership permission grants remain the only runtime authorization mechanism. No production SQL or secrets were accessed.

## Next

Build explicit screen-operation permission registry from approved policy, audited idempotent admin service and concurrency-safe persistence; reject self-escalation and cross-company references, verify negative HTTP/JWT and SQL acceptance. Then enable role policy only after complete snapshot and revocation acceptance; continue SYS09 then GL14/GL03.

## Verification evidence

- Local Release, traceability, HTTP and OpenAPI: 345/345 passed (no live SQL).
- [Branch CI #41422155947](https://github.com/ameenalqershi/ameen-erp/actions/runs/41422155947), commit e4fbec9be5fe9fa9173afc882c9cf518c7a0e984: 346/346 passed, zero failures, isolated SQL Server, HTTPS/JWT/OIDC, OpenAPI and Release. No new SQL migration; existing migration regressions were exercised.
- Verified source fingerprint: bb90be5e2d8f79dc0a30cee30d4663a98e02f7572c3350083a532bc7f19c59d9.
